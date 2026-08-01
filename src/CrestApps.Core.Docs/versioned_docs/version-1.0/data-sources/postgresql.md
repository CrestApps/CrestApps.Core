---
sidebar_label: PostgreSQL
sidebar_position: 4
title: PostgreSQL Provider
description: Configure PostgreSQL with pgvector as a lightweight vector search backend for RAG-powered AI experiences.
---

# PostgreSQL Provider

> Use PostgreSQL with the pgvector extension as a lightweight, self-hosted vector search backend for retrieval-augmented generation.

## Quick Start

### Using .NET Aspire (Recommended)

The easiest way to get started is with the Aspire AppHost, which automatically provisions a PostgreSQL instance with pgvector:

```bash
dotnet run --project src\Startup\CrestApps.Core.Aspire.AppHost\CrestApps.Core.Aspire.AppHost.csproj
```

The Aspire AppHost starts a PostgreSQL container (using the `pgvector/pgvector:pg16` image) and injects the `CrestApps__PostgreSQL__ConnectionString` environment variable into the MVC and Blazor sample hosts automatically. No manual configuration is needed.

### Manual Registration

```csharp
builder.Services.AddCorePostgreSQLServices(
    builder.Configuration.GetSection("CrestApps:PostgreSQL"));
```

Or without configuration binding:

```csharp
builder.Services.AddCorePostgreSQLServices();
```

## Configuration

### `appsettings.json`

```json
{
  "CrestApps": {
    "PostgreSQL": {
      "ConnectionString": "Host=localhost;Port=5432;Database=vectordb;Username=postgres;Password=your-password",
      "IndexPrefix": ""
    }
  }
}
```

### `PostgreSQLConnectionOptions`

| Property | Type | Description |
|----------|------|-------------|
| `ConnectionString` | `string` | PostgreSQL connection string |
| `IndexPrefix` | `string` | Optional prefix for all table names (useful for multi-tenant setups) |

## Services Registered (Keyed by `"PostgreSQL"`)

| Service | Implementation |
|---------|---------------|
| `IDataSourceContentManager` | `PostgreSQLDataSourceContentManager` |
| `IDataSourceDocumentReader` | `DataSourcePostgreSQLDocumentReader` |
| `IODataFilterTranslator` | `PostgreSQLODataFilterTranslator` |
| `ISearchIndexManager` | `PostgreSQLSearchIndexManager` |
| `ISearchDocumentManager` | `PostgreSQLSearchDocumentManager` |

When the `ConnectionString` is provided, an `IPostgreSQLClientFactory` singleton is also registered, which manages `NpgsqlDataSource` instances.

AI-specific PostgreSQL registrations live in `CrestApps.Core.AI.PostgreSQL`. Register that package when you need `AddAIDocuments()`, `AddAIDataSources()`, `AddAIMemory()`, or PostgreSQL-backed AI RAG/search flows.

When you call `AddAIDataSources()`, the feature builder also pulls in the shared asynchronous data-source synchronization stack from `AddCoreAIDataSourceRag()`, including:

- `IAIDataSourceIndexingQueue`
- `IAIDataSourceIndexingService`
- `AIDataSourceCatalogIndexingHandler`
- `AIDataSourceSearchDocumentHandler`
- `AIDataSourceIndexingBackgroundService`
- `AIDataSourceAlignmentBackgroundService`
- `DataSourceSearchIndexProfileHandler`

Override `IAIDataSourceIndexingQueue` when you need a durable or distributed queue, override `IAIDataSourceIndexingService` when you need different synchronization rules, and add your own `ISearchDocumentHandler` registrations when source-index writes should trigger additional asynchronous work.

## External PostgreSQL source mappings

When an `AIDataSource` uses `Source = "PostgreSQL"`, the mapping reads documents from a remote PostgreSQL table using source-specific settings stored on the `AIDataSource`:

- `ConnectionString` (protected at rest)
- `TableName`

This source-side configuration is separate from the shared PostgreSQL backend registration used for the knowledge-base vector store.

Because the source table is externally managed, record changes are synchronized by calling `IAIDataSourceChangeNotifier` from your application or integration layer. See [Custom Sources](./custom-sources.md) for the notification pattern.

## Prerequisites

PostgreSQL must have the **pgvector** extension installed and enabled. The framework automatically runs `CREATE EXTENSION IF NOT EXISTS vector` when creating indexes.

### Minimum Requirements

- PostgreSQL 12+
- pgvector extension 0.5.0+

## Docker Setup for Local Development

Use Docker Compose to run PostgreSQL with pgvector locally:

```yaml title="docker-compose.yml"
services:
  postgres:
    image: pgvector/pgvector:pg16
    container_name: postgres-vector
    environment:
      - POSTGRES_USER=postgres
      - POSTGRES_PASSWORD=changeme
      - POSTGRES_DB=vectordb
    ports:
      - "5432:5432"
    volumes:
      - pg-data:/var/lib/postgresql/data

volumes:
  pg-data:
    driver: local
```

Start it with:

```bash
docker compose up -d
```

:::tip
The `pgvector/pgvector` Docker image comes with the pgvector extension pre-installed. No additional setup is needed.
:::

Then configure your `appsettings.Development.json`:

```json
{
  "CrestApps": {
    "PostgreSQL": {
      "ConnectionString": "Host=localhost;Port=5432;Database=vectordb;Username=postgres;Password=changeme"
    }
  }
}
```

## Full Registration Example

```csharp
builder.Services
    .AddCoreServices()
    .AddCoreAIServices()
    .AddIndexingServices(indexing => indexing
        .AddYesSqlStores()
        .AddPostgreSQL(
            builder.Configuration.GetSection("CrestApps:PostgreSQL"),
            postgreSQL => postgreSQL
                .AddAIDocuments()
                .AddAIDataSources()
                .AddAIMemory()
        )
    );
```

## Configuration Reference

### Full `appsettings.json` Example

```json
{
  "CrestApps": {
    "PostgreSQL": {
      "ConnectionString": "Host=my-server.postgres.database.azure.com;Port=5432;Database=vectordb;Username=admin;Password=your-secure-password;Ssl Mode=Require;Trust Server Certificate=true",
      "IndexPrefix": "myapp_"
    }
  }
}
```

### `PostgreSQLConnectionOptions` — All Properties

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| `ConnectionString` | `string` | Yes | — | Standard PostgreSQL connection string. Supports all Npgsql connection string parameters. |
| `IndexPrefix` | `string` | No | `""` | Prefix prepended to all table names. Useful for multi-tenant deployments sharing a database. |

:::info
When `ConnectionString` is provided, the framework registers an `IPostgreSQLClientFactory` singleton that all keyed services share. If `ConnectionString` is empty or null, no client is registered and the data source is effectively disabled.
:::

## How It Works

### Index Schema

Each index profile maps to a separate PostgreSQL table. The table schema is derived from the `SearchIndexField` definitions:

| Field Type | PostgreSQL Column Type |
|------------|----------------------|
| Vector | `vector(N)` (pgvector) |
| Text | `TEXT` |
| Keyword | `TEXT` (with B-tree index) |
| Integer | `INTEGER` |
| Float | `DOUBLE PRECISION` |
| DateTime | `TIMESTAMP WITH TIME ZONE` |
| Boolean | `BOOLEAN` |

An additional `filters` column of type `JSONB` stores filterable metadata for OData queries.

### Vector Search

The provider uses pgvector's cosine distance operator (`<=>`) for k-NN (k-nearest neighbors) search:

```sql
SELECT *, 1 - (embedding <=> @query_vector) AS score
FROM my_index_table
WHERE filters @> '{"dataSourceId": "ds-123"}'
ORDER BY embedding <=> @query_vector
LIMIT 10
```

An IVFFlat index is created on vector columns for efficient approximate nearest-neighbor lookup.

### OData Filter Translation

OData filter expressions are translated to PostgreSQL WHERE clauses targeting the `filters` JSONB column:

| OData Expression | PostgreSQL Translation |
|-----------------|----------------------|
| `name eq 'value'` | `"filters"->>'name' = 'value'` |
| `age gt 21` | `("filters"->>'age')::numeric > 21` |
| `startswith(name, 'pre')` | `"filters"->>'name' LIKE 'pre%'` |
| `contains(name, 'mid')` | `"filters"->>'name' LIKE '%mid%'` |
| `endswith(name, 'suf')` | `"filters"->>'name' LIKE '%suf'` |
| `x eq 1 and y eq 2` | `... AND ...` |
| `x eq 1 or y eq 2` | `... OR ...` |

## Verification

After configuring the connection, verify it is working:

### 1. Check PostgreSQL and pgvector

```bash
# Connect to PostgreSQL
psql -h localhost -U postgres -d vectordb

# Verify pgvector extension
SELECT * FROM pg_extension WHERE extname = 'vector';
```

### 2. Verify from the Application

Inject `ISearchIndexManager` (keyed by `"PostgreSQL"`) and check if the connection is live:

```csharp
public sealed class PostgreSQLHealthCheck
{
    private readonly ISearchIndexManager _indexManager;

    public PostgreSQLHealthCheck(
        [FromKeyedServices("PostgreSQL")] ISearchIndexManager indexManager)
    {
        _indexManager = indexManager;
    }

    public async Task<bool> IsHealthyAsync()
    {
        // Attempt to check if a known index exists
        return await _indexManager.ExistsAsync("_test_ping");
    }
}
```

### 3. Check Tables Directly

```bash
# List all tables created by the provider
psql -h localhost -U postgres -d vectordb -c "\dt"

# Check a specific table schema
psql -h localhost -U postgres -d vectordb -c "\d your_index_table"
```

## Index Management

Indexes are created automatically when a data source is configured and content is indexed for the first time. The `PostgreSQLSearchIndexManager` handles index lifecycle:

- **Creation** — `CreateAsync()` defines the table schema with vector columns (pgvector `vector(N)` type), content fields, and a JSONB filters column. Creates an IVFFlat index on vector columns.
- **Existence check** — `ExistsAsync()` verifies a table is present before querying.
- **Deletion** — `DeleteAsync()` drops the table and all its data.

Table names are generated from the index profile name and include the configured prefix.

:::warning
Deleting an index drops the entire table and all indexed documents permanently. Re-indexing from the data source is required after deletion.
:::

## Comparison with Other Providers

| Feature | PostgreSQL | Elasticsearch | Azure AI Search |
|---------|-----------|---------------|-----------------|
| **Hosting** | Self-hosted or managed | Self-hosted or Elastic Cloud | Azure-managed |
| **Vector search** | pgvector (IVFFlat) | Dense vector (HNSW) | HNSW |
| **Setup complexity** | Low | Medium | Low (Azure) |
| **Cost** | Low (existing PostgreSQL) | Medium–High | Pay-per-use |
| **Best for** | Small–medium workloads, existing PostgreSQL infrastructure | Large-scale search, full-text + vector | Enterprise Azure environments |

## Troubleshooting

### Connection Refused

**Error:** `Npgsql.NpgsqlException: Failed to connect`

**Cause:** PostgreSQL is not running or the connection string is incorrect.

**Fix:**
- Verify PostgreSQL is running: `docker ps` or `pg_isready -h localhost`
- Check the `ConnectionString` in `appsettings.json`
- Ensure the port is correct (default: 5432)

### Authentication Failed

**Error:** `Npgsql.PostgresException: password authentication failed`

**Cause:** Invalid username or password.

**Fix:**
- Verify credentials in the connection string
- Check `pg_hba.conf` for authentication method settings

### pgvector Extension Not Found

**Error:** `Npgsql.PostgresException: extension "vector" is not available`

**Cause:** The pgvector extension is not installed on the PostgreSQL server.

**Fix:**
- Install pgvector: follow the [pgvector installation guide](https://github.com/pgvector/pgvector#installation)
- Use the `pgvector/pgvector` Docker image which includes it pre-installed
- On managed services (Azure, AWS RDS), enable the extension in the service configuration

### IVFFlat Index Warning

**Warning:** `Could not create IVFFlat index — table may be empty`

**Cause:** IVFFlat indexes require data in the table to determine list parameters.

**Fix:**
- This is informational. The index will be created lazily after data is inserted.
- Vector search still works without the IVFFlat index (uses sequential scan), but may be slower for large datasets.

### Table Already Exists

**Error:** When attempting to create an index that already exists.

**Cause:** The table was previously created and not cleaned up.

**Fix:**
- Use `DeleteAsync()` to drop the existing table, then recreate it
- Or verify the existing table schema matches expectations
