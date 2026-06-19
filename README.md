# Product Aggregator - Code Challenge

Servicio de agregación de productos que consulta múltiples proveedores externos (mock) para obtener precios y stock, y los combina en un único objeto `Product`.

## Escenario

El servicio consulta:

- **Precios** de 3 proveedores (`PRICE_A`, `PRICE_B`, `PRICE_C`)
- **Stock** de 2 regiones (`STOCK_EAST`, `STOCK_WEST`)

Cada proveedor simula latencia de red (150–700 ms). La implementación original procesaba todo **secuencialmente**, lo que generaba tiempos de respuesta muy altos.

## Optimizaciones implementadas

Cada paso tiene un **tag de git** para checkout y reproducir benchmarks.

| Parte | Descripción | Tag | Commit |
|-------|-------------|-----|--------|
| **1** | Paralelismo de proveedores por producto (`Task.WhenAll`) + propagación de `CancellationToken` | `paso1` | `7e7c6d8` |
| **2** | Paralelismo de productos con límite configurable (`Parallel.ForEachAsync`) | `paso2` | `9d6b0d5` |
| **3** | Logging estructurado + errores parciales (`ProviderErrors`, `Warnings`) | `paso3` | `df56c3b` |
| **4** | Caché en memoria por proveedor (`IMemoryCache` + decoradores) | `paso4` | `330a747` |
| **Factory + DI** | Unificación de proveedores vía `IProviderFactory` (sin duplicar instancias) Mejora de mantenibilidad | — | _(ver rama actual)_ |
| **Timeout** | Deadline configurable por proveedor (`ProviderTimeoutMs`) | — | _(ver rama actual)_ |
| **Compare** | Endpoint `/benchmark/compare` vs baseline documentado | — | _(ver rama actual)_ |
| **Tests** | Suite xUnit (`ProductAggregator.Core.Tests`) | — | _(ver rama actual)_ |

> **Nota sobre numeración:** los tags `paso1`–`paso4` siguen el orden de commits de rendimiento/features. Otras partes del `PLAN.md` (Factory, timeout, compare, tests) están en la rama actual.

## Estructura del proyecto

```
src/
├── ProductAggregator.Api/
│   ├── Controllers/ProductsController.cs
│   └── Program.cs
└── ProductAggregator.Core/
    ├── Models/
    ├── Interfaces/
    ├── Services/
    │   ├── ProductAggregatorService.cs
    │   ├── Caching/
    │   └── MockProviders/
    └── Factories/ProviderFactory.cs

tests/
└── ProductAggregator.Core.Tests/
    ├── ProductAggregatorServiceTests.cs
    ├── CachingPriceProviderTests.cs
    └── BenchmarkComparisonCalculatorTests.cs
```

## Cómo ejecutar

```bash
dotnet restore
cd src/ProductAggregator.Api
dotnet run
```

### Tests

```bash
dotnet test
```

### URLs locales

| Perfil | URL |
|--------|-----|
| HTTPS (default) | `https://localhost:7071` |
| HTTP | `http://localhost:5214` (redirige a HTTPS) |

En Windows, usar `curl -k` para omitir la validación del certificado de desarrollo.

## Configuración

`src/ProductAggregator.Api/appsettings.json`:

```json
{
  "Aggregation": {
    "MaxConcurrentProducts": 5,
    "CacheTtlSeconds": 30,
    "ProviderTimeoutMs": 2000
  }
}
```

| Opción | Default | Descripción |
|--------|---------|-------------|
| `MaxConcurrentProducts` | `5` | Máximo de productos procesados en paralelo |
| `CacheTtlSeconds` | `30` | TTL de caché por proveedor (segundos). `0` = desactivada |
| `ProviderTimeoutMs` | `2000` | Timeout por proveedor (ms). `0` = desactivado |

Con `MaxConcurrentProducts = 1` los productos se procesan secuencialmente (comportamiento equivalente a Parte 1 sola).

## Endpoints

### 1. Benchmark

```bash
curl -k "https://localhost:7071/api/products/benchmark?productCount=10"
```

Parámetro `productCount`: 1–20 (default 10).

Respuesta:

```json
{
  "productCount": 10,
  "processingTimeMs": 622,
  "successfulProducts": 10,
  "averageTimePerProduct": 62.2,
  "errors": []
}
```

> **Nota:** `averageTimePerProduct` es `processingTimeMs / productCount`. Con paralelismo de productos (Parte 2), el total baja mucho más que el promedio por producto; mirar siempre `processingTimeMs` para comparar rendimiento real.

### 2. Benchmark compare (vs baseline original)

```bash
curl -k "https://localhost:7071/api/products/benchmark/compare?productCount=10"
```

Compara el rendimiento **actual** contra el baseline secuencial documentado (mediciones del código original):

```json
{
  "productCount": 10,
  "current": {
    "processingTimeMs": 622,
    "averageTimePerProduct": 62.2
  },
  "estimatedBaseline": {
    "processingTimeMs": 20263,
    "averageTimePerProduct": 2026.3
  },
  "improvementPercent": 96.9
}
```

| Campo | Significado |
|-------|-------------|
| `current` | Resultado de la agregación optimizada actual |
| `estimatedBaseline` | Medición documentada del código original (`ba0803b`) |
| `improvementPercent` | `(baseline - current) / baseline × 100` |

Para `productCount` distinto de 1, 5 o 10, el baseline se extrapola linealmente (~2026 ms/producto).

### 3. Agregar múltiples productos

```bash
curl -k -X POST "https://localhost:7071/api/products/aggregate" \
  -H "Content-Type: application/json" \
  -d '{
    "productIds": ["PROD-001", "PROD-002", "PROD-003"],
    "includePrices": true,
    "includeStock": true
  }'
```

Respuesta extendida (`paso3` — observabilidad):

```json
{
  "products": [ ... ],
  "totalRequested": 3,
  "totalSuccessful": 3,
  "processingTimeMs": 850,
  "errors": [],
  "providerErrors": [],
  "warnings": []
}
```

| Campo | Significado |
|-------|-------------|
| `errors` | Producto **no procesado** (fallo total) |
| `providerErrors` | Proveedor falló o hizo timeout — producto devuelto **parcialmente** |
| `warnings` | Mismo fallo parcial en texto legible |

### 4. Obtener un producto

```bash
curl -k "https://localhost:7071/api/products/PROD-001"
```

### 5. OpenAPI (Development)

```bash
curl -k "https://localhost:7071/openapi/v1.json"
```

## Resultados de rendimiento

Benchmarks medidos con `GET /api/products/benchmark` en entorno local (Windows, .NET 10, proveedores mock con latencia aleatoria). Cada celda es una ejecución representativa.

### Tiempo total — `GET /benchmark` (`processingTimeMs`)

| productCount | Original | `paso1` | `paso2` | Mejora vs Original (`paso2`) |
|--------------|----------|---------|---------|------------------------------|
| 1 | 1 711 ms | 536 ms | 568 ms | **67%** |
| 5 | 9 736 ms | 2 885 ms | 678 ms | **93%** |
| 10 | 20 263 ms | 5 534 ms | 622 ms | **97%** |

### Promedio por producto — `GET /benchmark` (`averageTimePerProduct`)

| productCount | Original | `paso1` | `paso2` |
|--------------|----------|---------|---------|
| 1 | 1 711 ms | 536 ms | 568 ms |
| 5 | 1 947 ms | 577 ms | 136 ms |
| 10 | 2 026 ms | 553 ms | 62 ms |

### Caché — `POST /aggregate` con IDs repetidos (`paso4`)

| Escenario | `processingTimeMs` | Notas |
|-----------|----------------------|-------|
| 1ª llamada (caché fría) | **699 ms** | Consulta real a los 5 proveedores |
| 2ª llamada (caché caliente, TTL 30s) | < **5 ms** | Respuestas servidas desde `IMemoryCache` |
| `GET /benchmark?productCount=10` en `paso4` | **700 ms** | IDs distintos → poca reutilización de caché |

### Compare endpoint — ejemplo (`productCount=10`)

| Métrica | Valor |
|---------|-------|
| Baseline original | 20 263 ms |
| Actual (típico) | ~622 ms |
| Mejora | **~97%** |

## Decisiones de diseño

### Paralelismo por producto (`Task.WhenAll`)

Los 3 proveedores de precio y 2 de stock son independientes. Ejecutarlos en paralelo reduce el tiempo por producto de ~suma(latencias) a ~max(latencias) sin cambiar el contrato HTTP.

### Paralelismo de productos (`Parallel.ForEachAsync`)

Con 50 productos y 5 proveedores, el paralelismo total podría disparar 250 llamadas simultáneas. `MaxConcurrentProducts` limita la concurrencia. Los resultados se acumulan por índice para **preservar el orden** de la request.

### Errores separados (`Errors` vs `ProviderErrors`)

- `Errors`: fallo total — el producto no se devuelve.
- `ProviderErrors`: fallo parcial o timeout — el producto se devuelve con datos incompletos.
- Esto evita que un fallo de `PRICE_A` (~5%) se interprete como fallo de toda la agregación.

### Caché por proveedor (Decorator + `IMemoryCache`)

- Cada mock se envuelve en un decorador que implementa `IPriceProvider` / `IStockProvider`.
- `ProductAggregatorService` no sabe que hay caché — solo llama a la interfaz vía factory.
- Se cachea **por proveedor**, no el producto agregado, para respetar `includePrices` / `includeStock`.

### Factory + DI (`IProviderFactory`)

Un solo camino de instancias: `Mock → CachingDecorator → ProviderFactory → Service`.

### Timeout por proveedor

Cada llamada usa `CancellationTokenSource.CreateLinkedTokenSource` + `CancelAfter(ProviderTimeoutMs)`. Los timeouts se reportan en `ProviderErrors` sin tumbar el producto.

### Baseline documentado (`PerformanceBaseline`)

El endpoint `/benchmark/compare` usa mediciones reales del código original (no re-ejecuta el código legacy). Valores exactos para 1, 5 y 10 productos; extrapolación lineal para otros counts.

## Cómo reproducir los benchmarks

```bash
git checkout ba0803b          # Original
git switch --detach paso1       # 7e7c6d8
git switch --detach paso2       # 9d6b0d5
git switch --detach paso3       # df56c3b
git switch --detach paso4       # 330a747
git checkout feature/performance-optimization
```

```bash
cd src/ProductAggregator.Api && dotnet run
```

```bash
curl -k "https://localhost:7071/api/products/benchmark?productCount=10"
curl -k "https://localhost:7071/api/products/benchmark/compare?productCount=10"
dotnet test
```

## Tareas originales del challenge

1. ~~Analizar el código en `ProductAggregatorService.cs`~~
2. ~~Identificar problemas de rendimiento~~
3. ~~Proponer e implementar mejoras~~

Ver `PLAN.md` para optimizaciones pendientes (selección de providers, etc.).
