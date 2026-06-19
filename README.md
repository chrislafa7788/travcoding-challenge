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
    │   ├── Caching/                    # Parte 4: decoradores de caché
    │   │   ├── CachingPriceProvider.cs
    │   │   ├── CachingStockProvider.cs
    │   │   └── ProviderCacheKeys.cs
    │   └── MockProviders/
    └── Factories/ProviderFactory.cs
```

## Cómo ejecutar

```bash
dotnet restore
cd src/ProductAggregator.Api
dotnet run
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
    "CacheTtlSeconds": 30
  }
}
```

| Opción | Default | Descripción |
|--------|---------|-------------|
| `MaxConcurrentProducts` | `5` | Máximo de productos procesados en paralelo |
| `CacheTtlSeconds` | `30` | TTL de caché por proveedor (segundos). `0` = desactivada |

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

### 2. Agregar múltiples productos

```bash
curl -k -X POST "https://localhost:7071/api/products/aggregate" \
  -H "Content-Type: application/json" \
  -d '{
    "productIds": ["PROD-001", "PROD-002", "PROD-003"],
    "includePrices": true,
    "includeStock": true
  }'
```

Respuesta extendida (Parte 3):

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
| `providerErrors` | Proveedor falló, producto devuelto **parcialmente** |
| `warnings` | Mismo fallo parcial en texto legible |

### 3. Obtener un producto

```bash
curl -k "https://localhost:7071/api/products/PROD-001"
```

### 4. OpenAPI (Development)

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

Medido en tag `paso4` (`330a747`) con body:

```json
{
  "productIds": ["PROD-001", "PROD-001", "PROD-001"],
  "includePrices": true,
  "includeStock": true
}
```

| Escenario | `processingTimeMs` | Notas |
|-----------|----------------------|-------|
| 1ª llamada (caché fría) | **699 ms** | Consulta real a los 5 proveedores |
| 2ª llamada (caché caliente, TTL 30s) | **0 ms** | Respuestas servidas desde `IMemoryCache` |
| `GET /benchmark?productCount=10` en `paso4` | **700 ms** | IDs distintos → poca reutilización de caché |

> El benchmark con IDs únicos (`PROD-0001`…`PROD-0010`) no muestra el beneficio de caché. Para verlo, repetir el mismo `productId` o ejecutar la misma request dos veces dentro del TTL.

**Comando para reproducir el benchmark de caché:**

```bash
git switch --detach paso4
cd src/ProductAggregator.Api && dotnet run
```

```bash
curl -k -X POST "https://localhost:7071/api/products/aggregate" \
  -H "Content-Type: application/json" \
  -d '{"productIds":["PROD-001","PROD-001","PROD-001"],"includePrices":true,"includeStock":true}'

# Repetir el mismo curl inmediatamente → processingTimeMs ≈ 0
```

### Qué explica cada mejora

**Original → `paso1` (~73% menos en 10 productos):**
- Antes: 5 llamadas secuenciales por producto (suma de latencias).
- Después: 5 llamadas en paralelo por producto (latencia ≈ la del proveedor más lento).

**`paso1` → `paso2` (~89% menos adicional en 10 productos):**
- Antes: productos procesados uno tras otro.
- Después: hasta 5 productos en paralelo (`MaxConcurrentProducts = 5` en el commit medido).

**`paso2` → `paso4` (caché, requests repetidas):**
- Decoradores `CachingPriceProvider` / `CachingStockProvider` cachean por clave `{ProviderId}:{productId}:price|stock`.
- Solo respuestas exitosas se guardan; fallos no se cachean.
- 2ª request idéntica dentro del TTL: de ~700 ms a ~0 ms.

**Comportamiento esperado por fase:**

```
Original:  O(productos) × O(proveedores) secuencial  →  ~2 s/producto
paso1:     O(productos) × O(1) por producto           →  ~0.5–0.7 s/producto
paso2:     O(productos / concurrencia) × O(1)         →  ~0.6–1.2 s total para 10 productos
paso4:     paso2 + caché en hits                      →  ~0 ms en re-llamadas dentro del TTL
```

## Decisiones de diseño

### Paralelismo por producto (`Task.WhenAll`)

Los 3 proveedores de precio y 2 de stock son independientes. Ejecutarlos en paralelo reduce el tiempo por producto de ~suma(latencias) a ~max(latencias) sin cambiar el contrato HTTP.

### Paralelismo de productos (`Parallel.ForEachAsync`)

Con 50 productos y 5 proveedores, el paralelismo total podría disparar 250 llamadas simultáneas. `MaxConcurrentProducts` limita la concurrencia. Los resultados se acumulan por índice para **preservar el orden** de la request.

### Errores separados (`Errors` vs `ProviderErrors`)

- `Errors`: fallo total — el producto no se devuelve.
- `ProviderErrors`: fallo parcial — el producto se devuelve con datos incompletos.
- Esto evita que un fallo de `PRICE_A` (~5%) se interprete como fallo de toda la agregación.

### Caché por proveedor (Decorator + `IMemoryCache`)

- Cada mock se envuelve en un decorador que implementa `IPriceProvider` / `IStockProvider`.
- `ProductAggregatorService` no sabe que hay caché — solo llama a la interfaz.
- Se cachea **por proveedor**, no el producto agregado, para respetar `includePrices` / `includeStock`.
- Clave: `PRICE_A:PROD-001:price`, `STOCK_EAST:PROD-001:stock`, etc.

### `CancellationToken` propagado

El token viaja desde el controller hasta cada `GetPriceAsync` / `GetStockAsync`, permitiendo cancelar requests largas.

## Cómo reproducir los benchmarks

```bash
# Original (código sin optimizar)
git checkout ba0803b

# Parte 1 — paralelismo de proveedores
git switch --detach paso1    # 7e7c6d8

# Parte 2 — paralelismo de productos
git switch --detach paso2    # 9d6b0d5

# Parte 3 — observabilidad
git switch --detach paso3    # df56c3b

# Parte 4 — caché
git switch --detach paso4    # 330a747

# Rama actual (todas las optimizaciones)
git checkout feature/performance-optimization
```

En cada checkout:

```bash
cd src/ProductAggregator.Api
dotnet run
```

```bash
# Benchmark general (IDs únicos)
curl -k "https://localhost:7071/api/products/benchmark?productCount=1"
curl -k "https://localhost:7071/api/products/benchmark?productCount=5"
curl -k "https://localhost:7071/api/products/benchmark?productCount=10"

# Benchmark de caché (solo paso4+)
curl -k -X POST "https://localhost:7071/api/products/aggregate" \
  -H "Content-Type: application/json" \
  -d '{"productIds":["PROD-001","PROD-001"],"includePrices":true,"includeStock":true}'
# Ejecutar dos veces y comparar processingTimeMs
```

## Tareas originales del challenge

1. ~~Analizar el código en `ProductAggregatorService.cs`~~
2. ~~Identificar problemas de rendimiento~~
3. ~~Proponer e implementar mejoras~~

Ver `PLAN.md` para el roadmap de optimizaciones pendientes (timeout, tests, etc.).
