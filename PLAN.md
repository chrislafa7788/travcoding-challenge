# Plan de implementación — Product Aggregator Challenge

> Documento de trabajo para implementar mejoras de rendimiento, resiliencia y features nuevas.
> Cada **Parte** está pensada para un commit independiente. Pedir "implementa la Parte N" cuando corresponda.

---

## Objetivo

Transformar `ProductAggregatorService` de un agregador **secuencial y lento** en uno **paralelo, cancelable, observable y extensible**, manteniendo compatibilidad con los endpoints existentes y demostrando mejoras medibles con `/api/products/benchmark`.

---

## Estado actual (baseline)

### Cuellos de botella confirmados

| # | Problema | Ubicación | Impacto |
|---|----------|-----------|---------|
| 1 | Productos procesados en serie | `AggregateProductsAsync` → `foreach` | O(N) en tiempo total |
| 2 | Proveedores de precio en serie | `GetProductInternalAsync` → `foreach` + `await` | ~3× latencia por producto |
| 3 | Proveedores de stock en serie | Idem | ~2× latencia adicional |
| 4 | Precios y stock también en serie entre sí | Idem | Suma en lugar de max(paralelo) |
| 5 | `CancellationToken` no propagado | Controller → Service → Providers | Requests canceladas siguen ejecutándose |
| 6 | ~~`ProviderFactory` duplicada y sin uso~~ | ~~`Program.cs` + `ProviderFactory.cs`~~ | ~~Resuelto en Parte 3 (Factory + DI)~~ |
| 7 | ~~Sin caché~~ | ~~N/A~~ | ~~Resuelto en Parte 4 (`paso4`)~~ |
| 8 | Errores silenciados | `catch { }` vacío en providers | Difícil diagnosticar fallos parciales |
| 9 | Sin tests automatizados | N/A | Regresiones no detectables |

### Métricas baseline (referencia)

Medidas con `GET /api/products/benchmark` en entorno local (Windows, .NET 10):

```bash
curl -k "https://localhost:7071/api/products/benchmark?productCount=1"
curl -k "https://localhost:7071/api/products/benchmark?productCount=5"
curl -k "https://localhost:7071/api/products/benchmark?productCount=10"
```

#### Original (`ba0803b`)

| productCount | processingTimeMs | avg/product |
|--------------|------------------|-------------|
| 1 | 1 711 ms | 1 711 ms |
| 5 | 9 736 ms | 1 947 ms |
| 10 | 20 263 ms | 2 026 ms |

#### Parte 1 — paralelismo de proveedores (`7e7c6d8`)

| productCount | processingTimeMs | avg/product | Mejora vs Original |
|--------------|------------------|-------------|-------------------|
| 1 | 536 ms | 536 ms | 69% |
| 5 | 2 885 ms | 577 ms | 70% |
| 10 | 5 534 ms | 553 ms | 73% |

#### Parte 2 — paralelismo de productos (`9d6b0d5`, `MaxConcurrentProducts = 5`)

| productCount | processingTimeMs | avg/product | Mejora vs Original |
|--------------|------------------|-------------|-------------------|
| 1 | 568 ms | 568 ms | 67% |
| 5 | 678 ms | 136 ms | 93% |
| 10 | 622 ms | 62 ms | 97% |

**Meta orientativa post-optimización:** `processingTimeMs` para 10 productos ≪ 10 × latencia de un proveedor (~700 ms), gracias al paralelismo en ambos niveles. Con Parte 2 se midió **622 ms** total para 10 productos.

---

## Principios de diseño

1. **Cambios incrementales** — una parte = un commit revisable.
2. **Medir siempre** — benchmark antes/después de cada parte relevante.
3. **Compatibilidad** — no romper contratos HTTP existentes salvo extensión opt-in.
4. **Resiliencia** — fallo de un proveedor no tumba el producto ni la request completa.
5. **Control de concurrencia** — paralelizar sin saturar (semáforo / `ParallelOptions`).

---

## Parte 1 — Paralelismo de proveedores por producto

**Objetivo:** Reducir el tiempo por producto de ~suma(5 latencias) a ~max(5 latencias).

### Cambios

- [ ] Refactorizar `GetProductInternalAsync` para lanzar en paralelo:
  - Todas las llamadas a `IPriceProvider.GetPriceAsync`
  - Todas las llamadas a `IStockProvider.GetStockAsync`
  - Precios y stock entre sí también en paralelo (`Task.WhenAll`)
- [ ] Propagar `CancellationToken` desde `AggregateProductsAsync` y `GetProductAsync` hasta cada provider.
- [ ] Extraer método privado `FetchPricesAsync` / `FetchStockAsync` (o equivalente) para claridad.
- [ ] Mantener comportamiento de resiliencia: fallo individual → omitir ese provider, continuar.

### Archivos

| Archivo | Acción |
|---------|--------|
| `ProductAggregatorService.cs` | Modificar |

### Criterio de aceptación

- Benchmark con `productCount=1` baja ~60–70% vs baseline.
- Respuesta JSON mantiene misma estructura.
- Cancelar request interrumpe delays de mock providers.

### Commit sugerido

```
perf: parallelize provider calls per product

Run price and stock provider requests concurrently within each product
aggregation and propagate CancellationToken through the service layer.
```

---

## Parte 2 — Paralelismo de productos con límite de concurrencia

**Objetivo:** Procesar múltiples productos en paralelo sin disparar 50×5 llamadas simultáneas.

### Cambios

- [ ] Reemplazar `foreach` secuencial en `AggregateProductsAsync` por procesamiento paralelo controlado.
- [ ] Introducir `SemaphoreSlim` o `Parallel.ForEachAsync` con `MaxDegreeOfParallelism` configurable.
- [ ] Agregar opción en `appsettings.json`:

```json
"Aggregation": {
  "MaxConcurrentProducts": 5
}
```

- [ ] Registrar `IOptions<AggregationOptions>` en DI.
- [ ] Usar colección thread-safe o agregar resultados post-`await` (evitar race en `response.Products`).

### Archivos

| Archivo | Acción |
|---------|--------|
| `ProductAggregatorService.cs` | Modificar |
| `Models/AggregationOptions.cs` | Crear |
| `Program.cs` | Registrar options |
| `appsettings.json` | Agregar sección |

### Criterio de aceptación

- Benchmark `productCount=10` mejora significativamente vs Parte 1 sola.
- Con `MaxConcurrentProducts=1` comportamiento equivalente a Parte 1 (modo secuencial de productos).
- Sin errores de concurrencia en `Products` / `Errors`.

### Commit sugerido

```
perf: aggregate products concurrently with configurable limit

Process multiple product IDs in parallel using a semaphore-backed
concurrency limit driven by AggregationOptions.
```

---

## Parte 3 — Unificar acceso a proveedores (Factory + DI)

**Objetivo:** Eliminar duplicación entre `ProviderFactory` (instancias propias) y DI (instancias registradas).

### Cambios

- [x] Refactorizar `ProviderFactory` para recibir proveedores vía DI en lugar de `new MockPriceProviderA()`.
- [x] Opción A (recomendada): `ProductAggregatorService` usa `IProviderFactory` como única fuente.
- [x] Eliminar registros individuales duplicados en `Program.cs` si la factory los centraliza.
- [x] Mantener `GetPriceProvider(id)` / `GetStockProvider(id)` para uso futuro.

### Archivos

| Archivo | Acción |
|---------|--------|
| `ProviderFactory.cs` | Modificar constructor |
| `ProductAggregatorService.cs` | Inyectar `IProviderFactory` |
| `Program.cs` | Simplificar registros DI |

### Criterio de aceptación

- Una sola instancia de cada mock provider en runtime.
- Tests manuales: mismos resultados que antes.

### Commit sugerido

```
refactor: unify provider resolution through IProviderFactory

Inject registered provider instances into ProviderFactory and consume
providers exclusively through the factory in ProductAggregatorService.
```

---

## Parte 4 — Caché de respuestas de proveedores

**Objetivo:** Evitar re-llamadas costosas cuando el mismo `productId` se pide varias veces o en batch con IDs repetidos.

### Cambios

- [x] Crear `CachingPriceProvider` / `CachingStockProvider` decoradores, **o** caché dentro del servicio.
- [x] Usar `IMemoryCache` (ya referenciado en `.csproj`).
- [x] Clave de caché: `{ProviderId}:{productId}:{prices|stock}`.
- [x] TTL configurable en `appsettings.json`:

```json
"Aggregation": {
  "CacheTtlSeconds": 30
}
```

- [x] Respetar `includePrices` / `includeStock` (no cachear producto entero mezclado).

### Archivos

| Archivo | Acción |
|---------|--------|
| `Services/Caching/` o `Decorators/` | Crear decoradores |
| `Program.cs` | Registrar decoradores |
| `AggregationOptions.cs` | Agregar TTL |

### Criterio de aceptación

- Segunda llamada al mismo `productId` dentro del TTL → `processingTimeMs` drásticamente menor.
- Benchmark con IDs repetidos muestra mejora clara.

### Commit sugerido

```
feat: add in-memory cache for provider responses

Wrap price and stock providers with IMemoryCache decorators using a
configurable TTL to avoid redundant external calls.
```

---

## Parte 5 — Observabilidad y errores parciales enriquecidos

**Objetivo:** Saber *qué* falló sin romper la respuesta agregada.

### Cambios

- [ ] Reemplazar `catch { }` silencioso por logging estructurado (`ILogger<ProductAggregatorService>`).
- [ ] Extender `AggregatedProductResponse` con metadata opcional:

```csharp
public List<ProviderError> ProviderErrors { get; set; } = new();
// ProviderError: ProductId, ProviderId, Message
```

- [ ] Agregar campo `Warnings` para proveedores que fallaron pero el producto se devolvió parcialmente.
- [ ] Log por producto: duración, providers consultados, providers fallidos.

### Archivos

| Archivo | Acción |
|---------|--------|
| `Models/AggregatedProductRequest.cs` o nuevo modelo | Extender response |
| `ProductAggregatorService.cs` | Logging + errores parciales |

### Criterio de aceptación

- Fallo simulado de `PRICE_A` (~5%) aparece en logs y opcionalmente en `ProviderErrors`.
- Producto sigue devolviéndose con precios de B y C.

### Commit sugerido

```
feat: add structured logging and partial provider error reporting

Surface per-provider failures in the aggregation response and log
aggregation timing without failing the entire product.
```

---

## Parte 6 — Feature: timeout por proveedor

**Objetivo:** No esperar indefinidamente si un mock (o API real futura) se cuelga.

### Cambios

- [ ] Agregar `ProviderTimeoutMs` en `AggregationOptions` (default: 2000).
- [ ] Envolver cada llamada a provider con `CancellationTokenSource.CreateLinkedTokenSource` + `CancelAfter`.
- [ ] Registrar timeout como error parcial (Parte 5).

### Archivos

| Archivo | Acción |
|---------|--------|
| `ProductAggregatorService.cs` | Timeout wrapper |
| `AggregationOptions.cs` | Nueva propiedad |

### Commit sugerido

```
feat: enforce per-provider timeout during aggregation

Cancel slow provider calls after a configurable deadline and report
them as partial failures instead of blocking the entire aggregation.
```

---

## Parte 7 — Feature: selección de proveedores en la request

**Objetivo:** Permitir al cliente pedir solo ciertos proveedores (extensión del dominio).

### Cambios

- [ ] Extender `AggregatedProductRequest`:

```csharp
public List<string>? PriceProviderIds { get; set; }  // null = todos
public List<string>? StockProviderIds { get; set; }  // null = todos
```

- [ ] Filtrar providers vía `IProviderFactory.GetPriceProvider(id)`.
- [ ] Validar IDs desconocidos → 400 con mensaje claro en controller.

### Archivos

| Archivo | Acción |
|---------|--------|
| `AggregatedProductRequest.cs` | Extender |
| `ProductsController.cs` | Validación |
| `ProductAggregatorService.cs` | Filtrado |

### Commit sugerido

```
feat: allow clients to select specific price and stock providers

Add optional provider ID filters to the aggregate request with
validation for unknown provider identifiers.
```

---

## Parte 8 — Feature: endpoint de comparación before/after

**Objetivo:** Demostrar la mejora de forma explícita en la entrevista/review.

### Cambios

- [ ] Nuevo endpoint `GET /api/products/benchmark/compare?productCount=10`.
- [ ] Ejecutar agregación actual y registrar métricas (o mantener snapshot de baseline hardcodeado / config).
- [ ] Respuesta:

```json
{
  "productCount": 10,
  "current": { "processingTimeMs": 3200, "averageTimePerProduct": 320 },
  "estimatedBaseline": { "processingTimeMs": 18500, "averageTimePerProduct": 1850 },
  "improvementPercent": 82.7
}
```

- [ ] Alternativa más limpia: extraer `ISequentialAggregator` (legacy) vs `IParallelAggregator` — **solo si no complica demasiado**. Preferir calcular baseline teórico o guardar constante documentada.

### Commit sugerido

```
feat: add benchmark comparison endpoint

Expose a compare endpoint that reports current performance against the
documented sequential baseline for demo purposes.
```

---

## Parte 9 — Tests automatizados

**Objetivo:** Evitar regresiones en rendimiento lógico y comportamiento.

### Cambios

- [ ] Crear proyecto `ProductAggregator.Core.Tests` (xUnit).
- [ ] Tests unitarios:
  - Agregación con mocks sin delay (providers fake rápidos).
  - Verificar que se invocan todos los providers esperados.
  - Verificar resiliencia ante fallo de un provider.
  - Verificar propagación de `CancellationToken`.
  - Verificar filtrado por provider IDs (Parte 7).
  - Verificar caché hit (Parte 4).
- [ ] Test de integración opcional con `WebApplicationFactory`.

### Commit sugerido

```
test: add unit tests for aggregation service behavior

Cover parallel aggregation, provider failure resilience, cancellation,
and cache hit scenarios.
```

---

## Parte 10 — Documentación y README

**Objetivo:** Dejar el repo listo para review.

### Cambios

- [x] Actualizar `README.md`:
  - Puertos correctos (`https://localhost:7071`)
  - Nuevos endpoints y options
  - Tabla before/after con benchmarks reales
  - Sección "Decisiones de diseño"
- [x] Completar tabla baseline al inicio de este `PLAN.md`.

### Commit sugerido

```
docs: update README with performance results and new endpoints

Document correct local URLs, configuration options, and measured
benchmark improvements after optimization.
```

---

## Orden recomendado de implementación

```
Parte 1  → Paralelismo por producto          [impacto alto, riesgo bajo]
Parte 2  → Paralelismo de productos          [impacto alto, riesgo medio]
Parte 5  → Observabilidad                    [impacto medio, facilita debug]
Parte 3  → Unificar Factory/DI               [deuda técnica]
Parte 4  → Caché                             [impacto alto en repeats]
Parte 6  → Timeout                           [resiliencia]
Parte 7  → Selección de providers            [feature dominio]
Parte 8  → Benchmark compare                 [demo]
Parte 9  → Tests                             [calidad]
Parte 10 → Docs                              [cierre]
```

> **MVP del challenge (mínimo para aprobar):** Partes **1 + 2 + 5**. El resto demuestra seniority.

---

## Riesgos y decisiones abiertas

| Decisión | Opciones | Recomendación |
|----------|----------|---------------|
| Paralelismo de productos | Sin límite vs semáforo | Semáforo (default 5) |
| Caché | Decorador vs servicio | Decorador (SRP, testeable) |
| Factory | Service usa factory vs IEnumerable | Factory como única fuente ✅ |
| Errores parciales | Solo logs vs campo en response | Ambos |
| Compare endpoint | Legacy code vs constante | Constante documentada (menos código muerto) |

---

## Checklist final antes de entregar

- [x] Benchmark `productCount=10` mejorado vs baseline (20 263 ms → 622 ms, **97%**).
- [x] `CancellationToken` propagado end-to-end.
- [x] Sin duplicación de instancias de providers.
- [x] README con URLs correctas.
- [ ] Tests pasando (`dotnet test`).
- [x] No secrets ni cambios fuera de scope.

---

## Cómo usar este plan

En el chat, pedir:

> "Implementa la **Parte 1** y haz commit"

Se implementará únicamente el scope de esa parte, se correrá benchmark si aplica, y se creará el commit con el mensaje sugerido (o adaptado).
