# PostgreSQL 18 vectorization demo (C#)

The same demo as [MAF-CosmosDB-Demo](../MAF-CosmosDB-Demo/README.md), with PostgreSQL 18 and `pgvector` in place of Azure Cosmos DB. It has the same menu, the same [products.json](data/products.json), the same sample questions and the same Microsoft Agent Framework 1.28.0 agent, so you can run the two side by side during your session.

![PostgreSQL demo flow: products.json is imported into the PostgreSQL products table in steps 1 to 3, and text-embedding-3-small adds a vector to each row in step 5. In steps 7 and 8, search text is embedded and searched with the <=> operator, which returns the top 4 rows. In step 9, a question goes to the Agent Framework agent, which calls the search_catalog tool to embed it and run the <=> operator, gets the top 4 rows back, and returns a grounded answer.](media/postgresql-flow.png)

## Part 1: Which AI extensions do you need?

**Only one: `pgvector`.** PostgreSQL stores and searches the vectors, and the C# app creates them by calling your `text-embedding-3-small` deployment. This is the same split as in the Cosmos DB demo.

| Extension | Needed? | What it does |
| --- | --- | --- |
| `pgvector` (`CREATE EXTENSION vector`) | **Yes** | Adds the `vector` data type, distance operators such as `<=>`, and the HNSW index. |
| `azure_ai` | No | Calls Azure OpenAI from inside SQL. Microsoft documents it for Azure Database for PostgreSQL, not for a server you run yourself, so it isn't used here. |
| `pgai` | No | Runs embedding from the database using a separate worker. The install steps found were written for Unix-style tools, with no Windows instructions. |

## Part 2: Setup

### Step 1: Check the prerequisites

| You need | Status on this machine when this was written |
| --- | --- |
| PostgreSQL 18 | 18.6 is installed and running on port 5432. |
| .NET 11 SDK | Installed. |
| Visual Studio C++ build tools | Build Tools 2026 with the C++ tools is installed. |
| PostgreSQL development headers | Installed, in `C:\Program Files\PostgreSQL\18\include`. |
| Git | Installed. |
| Your `postgres` password | The one you chose when you installed PostgreSQL. |
| Foundry deployments of `text-embedding-3-small` and `gpt-6-luna` | The same deployments as the Cosmos DB demo. |

### Step 2: Install pgvector

PostgreSQL 18 on Windows doesn't include `pgvector`, so you build it from source. This takes about a minute.

1. Open the Start menu, type `PowerShell`, right-click it and choose **Run as administrator**. Administrator rights are needed because the files go in `C:\Program Files`.
2. Read [scripts/install-pgvector.ps1](scripts/install-pgvector.ps1) if you want to see what it does. It clones the official `pgvector` v0.8.7 tag, compiles it, and copies it into the PostgreSQL folder. It doesn't restart the server, change settings or touch any database.
3. Run it:

```bash
powershell -ExecutionPolicy Bypass -File X:\MAF-DBVectorization-Demos\MAF-Postgresql-Demo\scripts\install-pgvector.ps1
```

4. Look for the green message `pgvector v0.8.7 is installed`. PostgreSQL doesn't need a restart.
5. Confirm that PostgreSQL can see it. A row with `vector` and `0.8.7` means it worked:

```bash
& "C:\Program Files\PostgreSQL\18\bin\psql.exe" -U postgres -c "SELECT name, default_version FROM pg_available_extensions WHERE name = 'vector';"
```

**To install it manually instead:** open **x64 Native Tools Command Prompt for VS** as administrator, and run these commands one at a time:

```bash
set "PGROOT=C:\Program Files\PostgreSQL\18"
```

```bash
cd %TEMP%
```

```bash
git clone --branch v0.8.7 https://github.com/pgvector/pgvector.git
```

```bash
cd pgvector
```

```bash
nmake /F Makefile.win
```

```bash
nmake /F Makefile.win install
```

You don't run `CREATE EXTENSION` yourself. Installing puts the files on the server, and step 2 of the demo enables the extension in the demo database.

### Step 3: Confirm your Azure OpenAI settings

This project shares its user secrets with the Cosmos DB demo, so `AzureOpenAI:Endpoint`, `AzureOpenAI:ApiKey` and any deployment names you set there already apply. There's nothing to re-enter.

### Step 4: Store your PostgreSQL password

Run this in this folder:

```bash
dotnet user-secrets set "Postgres:Password" "YOUR-POSTGRES-PASSWORD"
```

Optional settings, with their defaults:

| Setting | Default |
| --- | --- |
| `Postgres:Host` | `localhost` |
| `Postgres:Port` | `5432` |
| `Postgres:Username` | `postgres` |
| `Postgres:Database` | `vectordemo` |

### Step 5: Run the app

```bash
dotnet run
```

## Part 3: Running the demo

The steps are numbered the same as in the Cosmos DB demo. Press **Enter** at any prompt to accept the suggested text. **Before the demo starts**, choose `R` if you've run it earlier.

### 1. Show the source JSON

**What happens:** The app lists the 12 products and prints one full description.

**Say:** "This is exactly the same file I gave to Cosmos DB."

### 2. Create the table with a vector column

**What happens:** The app creates the `vectordemo` database if needed, enables the extension, creates the table and builds the index. Show this in [Demo.cs](Demo.cs):

```sql
CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE products (
    id text PRIMARY KEY,
    -- the other product columns
    embedding vector(1536)
);

CREATE INDEX products_embedding_idx ON products USING hnsw (embedding vector_cosine_ops);
```

**Say:** "In Cosmos DB I declared a policy on a container. Here a vector is simply a column type, and the index uses normal `CREATE INDEX` syntax."

| Setting | Value | Why |
| --- | --- | --- |
| Column type | `vector(1536)` | Must match what `text-embedding-3-small` produces. |
| Index type | `hnsw` | Stays fast as the table grows. |
| Operator class | `vector_cosine_ops` | Makes the index serve cosine distance, the `<=>` operator. |

**Good to know:** the `vector` type can be indexed with HNSW up to 2,000 dimensions, which covers 1536. With only 12 rows, PostgreSQL may scan the table instead of using the index, so **this demo doesn't show HNSW's speed advantage**. Don't claim that it does.

**Show in pgAdmin:** `vectordemo` > **Schemas** > **public** > **Tables** > **products** > **Columns**, and also **Extensions** > `vector`.

### 3. Import the documents

**What happens:** Each JSON document becomes a row. Scalar values go into typed columns, and `features` goes into a `text[]` array.

**Show in pgAdmin:** right-click `products` > **View/Edit Data** > **All Rows**. The `embedding` column is `NULL`.

### 4. Keyword search: the "before" picture

**What happens:** Press Enter to search for `something to keep my feet dry on a rainy walk`. The query removes filler words and uses `ILIKE` to require every remaining word. It finds nothing.

**Say:** "Same result as Cosmos DB. This isn't a database problem. It's what happens when the customer's words and our words differ."

### 5. Vectorize the documents

**What happens:** The app reads the rows, sends their text to `text-embedding-3-small` in one batch, and updates each row with its vector.

**Show in pgAdmin:** refresh the data view. `embedding` now holds a 1,536-number array, and the other columns haven't changed.

### 6. Inspect a vector

**What happens:** The app prints the start of one vector and its stored size in bytes.

### 7. Compare keyword search and vector search

**What happens:** The app runs both searches on the same text. Press Enter to move through the samples. Scores use the same scale as the Cosmos DB demo, because both are cosine similarity.

| Sample search | Expected vector match | What it proves |
| --- | --- | --- |
| `I get cold at night when camping in winter` | Polaris sleeping bag | Matches meaning: the text says *sub-zero nights*. |
| `how do I make a hot meal far from any town` | Ember stove | Works from a need, not a product name. |
| `necesito algo para purificar agua en la montaña` | ClearStream filter | A Spanish search finds English text. |
| `how can I call for help when I have no phone signal` | SafeGuide messenger | Understands the whole intent. |

Expected vector matches are predictions until you've done your rehearsal run. The vector query is ordinary SQL:

```sql
SELECT id, name, price, 1 - (embedding <=> @vector) AS score
FROM products
WHERE embedding IS NOT NULL
ORDER BY embedding <=> @vector
LIMIT 4;
```

`<=>` is cosine **distance**, where smaller is closer. Subtracting it from 1 gives cosine **similarity**, where closer to 1 is better.

### 8. Vector search with a price filter

**What happens:** Press Enter twice to search for `a shelter for two people` at $300 or less. The $649 Fjord tent is filtered out, and the $279 Wisp tent stays.

**Say:** "It's plain SQL, so it can also join to your orders, customers or inventory tables."

### 9. Ask the agent about the data

**What happens:** The agent code is the same as in the Cosmos DB demo, apart from the query inside `search_catalog`.

**Say:** "I swapped the database and the agent didn't notice. Its instructions, its tool and its model are all unchanged."

| Question | What it proves |
| --- | --- |
| Which tent should I buy for a stormy mountain trip, and what is its warranty? | Real product and warranty details come from your data. |
| How much does that one weigh? | The agent remembers the conversation. |
| What is the best shelter for two if my budget is $300? | The agent should pass the budget to the tool as `maxPrice`. |
| I'm planning a two-week winter trek. Which sleeping bag and pack do you recommend, and what do they cost? | The answer combines several rows. |
| Can your water filter make water safe from viruses? | It finds a caveat buried in a description. |
| Do you sell surfboards? | It should say no instead of inventing a product. |

## Part 4: Cosmos DB and PostgreSQL compared

| Topic | Azure Cosmos DB for NoSQL | PostgreSQL with pgvector |
| --- | --- | --- |
| Where it runs | Azure managed service | Anywhere, including this laptop |
| Enabling vectors | An account feature in the portal | Build or install an extension, then `CREATE EXTENSION` |
| Data shape | Schemaless JSON documents | Typed columns, defined up front |
| Declaring a vector | A vector policy on the container | A `vector(1536)` column |
| Default index in this demo | DiskANN, up to 4,096 dimensions | HNSW, up to 2,000 dimensions with `vector` |
| Query | `ORDER BY VectorDistance(c.embedding, @v)` | `ORDER BY embedding <=> @v` |
| Combine with filters | `WHERE` in the same query | `WHERE` in the same query, plus joins |
| Cost shown in the demo | RUs for each operation | Milliseconds, because local queries have no per-request charge |
| Scaling | Built-in partitioning and global distribution | Your server, unless you add a managed service or more tooling |
| Embedding creation | The app calls the model | The app calls the model |
| Agent code | Identical | Identical |

**A fair summary:** the AI workflow is the same in both. The choice depends on where your data already lives and who runs the platform, not on which one can do vector search.

**Filters at scale:** with an HNSW index, a `WHERE` filter can return fewer rows than `LIMIT` when many nearby rows fail the filter. `pgvector` 0.8 added `SET hnsw.iterative_scan = strict_order` for this. It makes no difference with 12 rows, so the demo doesn't set it.

**If someone asks about keyword search:** the `ILIKE` comparison is deliberately simple. PostgreSQL also has built-in full-text search, which would do better than `ILIKE`, and it can be combined with vector search.

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| `The pgvector extension isn't installed on this server` | Complete setup step 2. |
| `Missing setting 'Postgres:Password'` | Complete setup step 4 from this folder. |
| `password authentication failed` | Check `Postgres:Password`. |
| `The script ... cannot be run because it contains a "#requires" statement` | Open PowerShell with **Run as administrator**. |
| `postgres.h: No such file or directory` during the build | Check that `-PgRoot` points at your install, which is `C:\Program Files\PostgreSQL\18` by default. |
| `database "vectordemo" does not exist` | Run demo step 2 first. |
| `relation "products" does not exist` | Run demo step 2. |
| `expected 1536 dimensions, not ...` | The embedding deployment isn't using 1536 dimensions. Check `AzureOpenAI:EmbeddingDeployment`. |
| 401, 403 or 404 from Azure OpenAI | See the troubleshooting table in the [Cosmos DB README](../MAF-CosmosDB-Demo/README.md#troubleshooting). |
| Vector search returns no rows | Run step 5. Running step 3 again clears the vectors, so run step 5 after any re-import. |

## Cleaning up

Choose `R` to delete the table. To remove everything, run this:

```bash
& "C:\Program Files\PostgreSQL\18\bin\psql.exe" -U postgres -c "DROP DATABASE vectordemo;"
```

## Project files

| File | Purpose |
| --- | --- |
| [data/products.json](data/products.json) | An exact copy of the Cosmos DB demo's data |
| [Program.cs](Program.cs) | Menu and sample questions |
| [Demo.cs](Demo.cs) | The PostgreSQL logic, the agents and the `search_catalog` tool, one method per step |
| [Product.cs](Product.cs) | Product model and the text sent for embedding |
| [Settings.cs](Settings.cs) | Reads user secrets and environment variables |
| [Ui.cs](Ui.cs) | Console formatting helpers |
| [scripts/install-pgvector.ps1](scripts/install-pgvector.ps1) | Builds and installs `pgvector` as administrator |

## Sources

- [pgvector on GitHub](https://github.com/pgvector/pgvector)
- [pgvector 0.8.5 installation steps](https://docs.pgedge.com/pgvector/v0-8-5/installation/)
- [pgvector HNSW limits](https://pgxn.org/dist/vector/0.8.0/README.html)
- [HNSW indexes and iterative scans](https://supabase.com/docs/guides/ai/vector-indexes/hnsw-indexes.md)
- [azure_ai extension](https://learn.microsoft.com/en-us/Azure/postgresql/flexible-server/generative-ai-azure-openai)
- [pgai on GitHub](https://github.com/timescale/pgai)

## Note: what the `<=>` operator means

`<=>` is the **cosine distance** operator. It comes from the `pgvector` extension, not from core PostgreSQL. It measures how far apart two vectors point, where a smaller value means a closer match:

- `0` means the vectors point the same way (most similar).
- `1` means they are unrelated (orthogonal).
- `2` means they point in opposite directions.

The search query in [Demo.cs](Demo.cs) uses it twice:

```sql
SELECT ..., 1 - (embedding <=> @vector) AS score   -- turn distance into similarity
FROM products
WHERE embedding IS NOT NULL
ORDER BY embedding <=> @vector                     -- nearest products first
LIMIT 4
```

- **`ORDER BY embedding <=> @vector`** sorts products by how close their embedding is to the embedded search text, nearest first. The HNSW index uses `vector_cosine_ops`, so it can speed up this ordering.
- **`1 - (embedding <=> @vector)`** turns distance into cosine **similarity**, where higher is better. This gives the same kind of score as `VectorDistance` in the Cosmos DB demo, so results from the two demos can be compared directly.

pgvector has other distance operators too, but this demo only uses `<=>`:

| Operator | Meaning |
| --- | --- |
| `<->` | Euclidean (L2) distance |
| `<#>` | Negative inner product |
| `<=>` | Cosine distance |
| `<+>` | Manhattan (L1) distance |
