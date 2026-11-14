# SQL Server 2025 Express vectorization demo (C#)

The same demo as [MAF-CosmosDB-Demo](../MAF-CosmosDB-Demo/README.md) and [MAF-Postgresql-Demo](../MAF-Postgresql-Demo/README.md), with SQL Server 2025 Express as the database. It has the same menu, the same [products.json](data/products.json), the same sample questions and the same Microsoft Agent Framework 1.28.0 agent, so you can run all three side by side during your session.

![SQL Server 2025 demo flow: products.json is imported into the dbo.Products table in steps 1 to 3, and text-embedding-3-small adds a vector to each row in step 5. In steps 7 and 8, search text is embedded and searched with VECTOR_DISTANCE, which returns the top 4 rows. In step 9, a question goes to the Agent Framework agent, which calls the search_catalog tool to embed it and run VECTOR_DISTANCE, gets the top 4 rows back, and returns a grounded answer.](media/sqlserver-flow.png)

## Part 1: Which AI extensions do you need?

**None.** Vector support is built into the SQL Server 2025 engine, including Express, so there's nothing to install on the server. The only addition is on the client: the `Microsoft.Data.SqlClient` NuGet package, version 6.1.0 or later, which this project restores for you (it uses 7.1.2).

| Feature | Available on Express | Status | Used in this demo? |
| --- | --- | --- | --- |
| `VECTOR` data type (float32) | Yes | Works with no flags | **Yes** |
| `VECTOR_DISTANCE` (exact search) | Yes | Works with no flags | **Yes** |
| `json` data type | Yes | Works with no flags | **Yes**, for `Features` |
| DiskANN vector index and `VECTOR_SEARCH` | Yes | Preview, with limits on this build (see below) | No |
| External models and `AI_GENERATE_EMBEDDINGS` | Yes | Off until you enable a server setting | No, see Part 5 |
| Local ONNX models | Yes | Preview | No |

The first four rows of the table were checked on this machine, and the rest come from Microsoft's edition documentation.

**Why this demo has no vector index.** These results are from this machine, SQL Server 2025 RTM (17.0.1000.7):

- `CREATE VECTOR INDEX` needs `PREVIEW_FEATURES = ON` for the database.
- Once the index exists, the table is **read-only**. `INSERT`, `UPDATE` and even a price change all fail.
- The table must have a clustered primary key on a single `int` column, and `Products` uses the text IDs from the JSON.
- Microsoft's documentation says newer index versions remove the read-only limit and need at least 100 rows.

`VECTOR_DISTANCE` is exact, needs no flags, and suits a 12-row table. It scans every row, though, so a large table would need the index.

**Express limits to mention:** a 50 GB database, 1,410 MB of buffer pool memory, and the lesser of 1 socket or 4 cores.

## Part 2: Setup

### Step 1: Check the prerequisites

| You need | Status on this machine when this was written |
| --- | --- |
| SQL Server 2025 Express | Installed and running as `localhost\SQLEXPRESS`. |
| .NET 11 SDK | Installed. |
| Windows sign-in with rights to create a database | Your account is a sysadmin on the instance. |
| Foundry deployments of `text-embedding-3-small` and `gpt-6-luna` | The same deployments as the other demos. |

### Step 2: Check your Azure OpenAI settings

This project shares its user secrets with the other two demos, so `AzureOpenAI:Endpoint`, `AzureOpenAI:ApiKey` and any deployment names you set there already apply. There's nothing to re-enter.

### Step 3: Check your SQL Server settings

There's no password to store, because the app signs in with your Windows account. These optional settings show the defaults:

| Setting | Default |
| --- | --- |
| `Sql:Server` | `localhost\SQLEXPRESS` |
| `Sql:Database` | `VectorDemo` |

### Step 4: Run the app

Run this in this folder:

```bash
dotnet run
```

## Part 3: Running the demo

The steps are numbered the same as in the other demos. Press **Enter** at any prompt to accept the suggested text. **Before the demo starts**, choose `R` if you've run it earlier.

### 1. Show the source JSON

**What happens:** The app lists the 12 products and prints one full description.

**Say:** "Third database, same file."

### 2. Create the table with a vector column

**What happens:** The app creates the `VectorDemo` database if needed, then the table. Show this in [Demo.cs](Demo.cs):

```sql
CREATE TABLE dbo.Products (
    Id        nvarchar(20) NOT NULL PRIMARY KEY,
    -- the other product columns
    Features  json NOT NULL,
    Embedding vector(1536) NULL
);
```

**Say:** "With PostgreSQL I built an extension first. With Cosmos DB I turned on an account feature. Here `vector` is a built-in type, so it's just another column."

**Show in SSMS:** connect to `localhost\SQLEXPRESS`, then expand **Databases** > `VectorDemo` > **Tables** > `dbo.Products` > **Columns**.

### 3. Import the documents

**What happens:** Each JSON document becomes a row. `Features` goes into a native `json` column.

**Show in SSMS:** run `SELECT * FROM dbo.Products;` in the `VectorDemo` database. `Embedding` is `NULL`.

### 4. Keyword search: the "before" picture

**What happens:** Press Enter to search for `something to keep my feet dry on a rainy walk`. The query removes filler words and uses `LIKE` to require every remaining word. It finds nothing.

### 5. Vectorize the documents

**What happens:** The app reads the rows, sends their text to `text-embedding-3-small` in one batch, and updates each row. Vectors travel in binary form through `SqlVector<float>`.

In the test run, the 12 rows used 2,217 tokens and the model call took under a second.

**Show in SSMS:** run the `SELECT` again. `Embedding` now holds a JSON-style array of numbers.

### 6. Inspect a vector

**What happens:** The app prints the start of one vector and its stored size, which was 6,152 bytes in the test run.

### 7. Compare keyword search and vector search

**What happens:** The app runs both searches on the same text. Press Enter to move through the samples. Keyword search found nothing for any of these, and these were the top vector results in the test run:

| Sample search | Top vector match | Score | What it proves |
| --- | --- | --- | --- |
| `I get cold at night when camping in winter` | Polaris -18 Down Mummy Bag | 0.465 | Matches meaning: the text says *sub-zero nights*. |
| `how do I make a hot meal far from any town` | Ember Titanium Canister Stove | 0.351 | Works from a need, not a product name. |
| `necesito algo para purificar agua en la montaña` | ClearStream Gravity Filter 4L | 0.448 | A Spanish search finds English text. |
| `how can I call for help when I have no phone signal` | SafeGuide Satellite Messenger | 0.431 | Understands the whole intent. |

**Explain the scores:** these aren't percentages. Scores around 0.3 to 0.5 are normal when a short question is compared with a long description, and what matters is the gap to the next result. With the stove sample, for example, the next result scored 0.245.

The vector query is ordinary T-SQL:

```sql
SELECT TOP (4) Id, Name, Price, 1 - VECTOR_DISTANCE('cosine', Embedding, @Vector) AS Score
FROM dbo.Products
WHERE Embedding IS NOT NULL
ORDER BY VECTOR_DISTANCE('cosine', Embedding, @Vector);
```

`VECTOR_DISTANCE` returns a distance, where smaller is closer. Subtracting it from 1 gives cosine similarity, the same scale as the other two demos.

### 8. Vector search with a price filter

**What happens:** Press Enter twice to search for `a shelter for two people` at $300 or less. The $649 Fjord tent is filtered out, and the $279 Wisp tent comes first.

**Say:** "It's plain T-SQL, so it works with the joins, views and stored procedures your team already has."

### 9. Ask the agent about the data

**What happens:** The agent code is the same as in the other demos, apart from the query inside `search_catalog`. These results are from the test run:

| Question | What happened |
| --- | --- |
| Which tent should I buy for a stormy mountain trip, and what is its warranty? | Without data, the model recommended a real tent the store doesn't sell. With data, it chose the Fjord 2P and quoted its warranty. |
| How much does that one weigh? | It understood "that one" and answered 3300 g. |
| What is the best shelter for two if my budget is $300? | It passed `maxPrice: 300` to the tool by itself, chose the Wisp UL 2, and warned that it isn't for snow. |
| I'm planning a two-week winter trek. Which sleeping bag and pack do you recommend, and what do they cost? | It combined the Polaris and the Atlas 65, for $688 in total. |
| Can your water filter make water safe from viruses? | It answered no, using a caveat buried in the description. |
| Do you sell surfboards? | It said the catalog doesn't list any. |

The model's wording will change from run to run.

## Part 4: All three databases compared

| Topic | Azure Cosmos DB for NoSQL | PostgreSQL + pgvector | SQL Server 2025 Express |
| --- | --- | --- | --- |
| Where it runs | Azure managed service | Anywhere | Windows or Linux, including this laptop |
| Enabling vectors | An account feature in the portal | Build or install an extension | Nothing: it's built in |
| Data shape | Schemaless JSON | Typed columns, `text[]` | Typed columns, native `json` |
| Declaring a vector | A vector policy | `vector(1536)` | `vector(1536)` |
| Largest vector | 4,096 dimensions when indexed | 2,000 dimensions when indexed with HNSW | 1,998 dimensions (float32) |
| Index in this demo | DiskANN | HNSW | None, exact search |
| Query | `VectorDistance(c.embedding, @v)` | `embedding <=> @v` | `VECTOR_DISTANCE('cosine', Embedding, @v)` |
| Works with filters | Yes | Yes, plus joins | Yes, plus joins |
| Sign-in in this demo | Account key | Password | Windows account |
| Metric shown | RUs and milliseconds | Milliseconds | Milliseconds |
| Agent code | Identical | Identical | Identical |

**A fair summary:** the AI workflow is the same in all three, and the app calls the model in all three. The choice depends on where your data already lives and who runs the platform.

**Don't compare speed from this demo.** Twelve rows don't show how any of these databases perform at scale, and the three use different index setups.

**If someone asks about keyword search:** the `LIKE` comparison is deliberately simple. SQL Server also has full-text search, and it can be combined with vector search.

## Part 5: Optional: generating embeddings inside SQL Server

SQL Server 2025 can also call an embedding model from T-SQL, with no extension, which is a useful talking point. The demo doesn't use this, so that all three apps stay the same.

**This script is untested.** On this machine `CREATE EXTERNAL MODEL` succeeded, and `AI_GENERATE_EMBEDDINGS` stopped at the disabled server setting. Enabling that setting lets SQL Server make outbound REST calls, so it's left off.

```sql
EXEC sp_configure 'external rest endpoint enabled', 1;
RECONFIGURE;

-- In VectorDemo:
CREATE MASTER KEY ENCRYPTION BY PASSWORD = 'A-STRONG-PASSWORD';

CREATE DATABASE SCOPED CREDENTIAL [https://YOUR-RESOURCE.openai.azure.com]
    WITH IDENTITY = 'HTTPEndpointHeaders', SECRET = '{"api-key":"YOUR-KEY"}';

CREATE EXTERNAL MODEL EmbeddingModel
WITH (
    LOCATION = 'https://YOUR-RESOURCE.openai.azure.com/openai/deployments/text-embedding-3-small/embeddings?api-version=2024-02-01',
    API_FORMAT = 'Azure OpenAI',
    MODEL_TYPE = EMBEDDINGS,
    MODEL = 'text-embedding-3-small',
    CREDENTIAL = [https://YOUR-RESOURCE.openai.azure.com]
);

SELECT AI_GENERATE_EMBEDDINGS(N'hello' USE MODEL EmbeddingModel);
```

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| `Missing setting 'AzureOpenAI:Endpoint'` | Run the setup from the [Cosmos DB README](../MAF-CosmosDB-Demo/README.md), or set the value with `dotnet user-secrets`. |
| `A network-related or instance-specific error` | Check that the `SQL Server (SQLEXPRESS)` service is running and that `Sql:Server` is right. |
| `Cannot open database "VectorDemo"` | Run demo step 2 first. |
| `Invalid object name 'dbo.Products'` | Run demo step 2. |
| `CREATE DATABASE permission denied` | Your Windows account needs rights to create a database. |
| `Data modification statement failed because table ... has a vector index` | Someone added a vector index. Drop it with `DROP INDEX`, or choose `R` and start again. |
| 401, 403 or 404 from Azure OpenAI | See the troubleshooting table in the [Cosmos DB README](../MAF-CosmosDB-Demo/README.md#troubleshooting). |
| Vector search returns no rows | Run step 5. Running step 3 again clears the vectors, so run step 5 after any re-import. |

## Cleaning up

Choose `R` to delete the table. To remove the database too, run this in SSMS:

```sql
DROP DATABASE VectorDemo;
```

## Project files

| File | Purpose |
| --- | --- |
| [data/products.json](data/products.json) | An exact copy of the data used by the other demos |
| [Program.cs](Program.cs) | Menu and sample questions |
| [Demo.cs](Demo.cs) | The SQL Server logic, the agents and the `search_catalog` tool, one method per step |
| [Product.cs](Product.cs) | Product model and the text sent for embedding |
| [Settings.cs](Settings.cs) | Reads user secrets and environment variables |
| [Ui.cs](Ui.cs) | Console formatting helpers |

`System.Security.Cryptography.Pkcs` is pinned to 10.0.15 in [SqlServerVectorDemo.csproj](SqlServerVectorDemo.csproj). The pin was added because `Microsoft.Data.SqlClient` 7.1.1 brought in version 9.0.18, which has known vulnerabilities. Version 7.1.2 brings in 9.0.21, which has none listed on NuGet, so the pin is now optional.

## Sources

- [Editions and supported features of SQL Server 2025](https://learn.microsoft.com/en-us/sql/sql-server/editions-and-components-of-sql-server-2025)
- [vector data type](https://learn.microsoft.com/en-us/sql/t-sql/data-types/vector-data-type)
- [VECTOR_SEARCH](https://learn.microsoft.com/en-us/sql/t-sql/functions/vector-search-transact-sql)
- [Vector data in SqlClient](https://learn.microsoft.com/en-us/sql/connect/ado-net/sql/vector-data-sql-server)
- [What's new in SQL Server 2025](https://learn.microsoft.com/en-us/sql/sql-server/what-s-new-in-sql-server-2025)
