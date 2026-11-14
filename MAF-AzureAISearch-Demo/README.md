# Azure AI Search vectorization demo (C#)

The same demo as the [Cosmos DB](../MAF-CosmosDB-Demo/README.md), [PostgreSQL](../MAF-Postgresql-Demo/README.md) and [SQL Server](../MAF-SQLServer-Demo/README.md) projects, with Azure AI Search as the store. It has the same menu, the same [products.json](data/products.json), the same sample questions and the same Microsoft Agent Framework 1.28.0 agent, so you can run all four side by side during your session.

![Azure AI Search demo flow: products.json is uploaded to the index in steps 1 to 3, and text-embedding-3-small merges a vector into each document in step 5. In steps 7 and 8, search text is embedded and sent as an HNSW vector query that returns the top 4 documents, and step 7 also shows hybrid keyword and vector search merged with RRF. In step 9, a question goes to the Agent Framework agent, which calls the search_catalog tool to embed and run a vector query, gets the top 4 documents back, and returns a grounded answer.](media/azure-ai-search-flow.png)

## Part 1: What's different about Azure AI Search?

**It's a search service, not a database.** The other three demos add vectors to the database that holds your data. Azure AI Search is a separate, purpose-built search engine that holds a searchable copy of it. That makes it the "separate store" design from the talk, with the trade-offs that go with it: excellent search features, and a second copy to keep current.

**There's nothing to install.** Vector search is built into the service on every tier, at no extra charge. You only need the `Azure.Search.Documents` NuGet package (12.0.0), which this project restores for you.

| Topic | In this demo |
| --- | --- |
| Where vectors live | A `Collection(Edm.Single)` field with 1,536 dimensions |
| Vector index | HNSW with cosine distance |
| Keyword search | BM25 full-text ranking, which the databases don't have |
| Hybrid search | Keyword and vector in one request, merged with Reciprocal Rank Fusion (RRF) |
| Who calls the embedding model | The C# app, the same as the other demos |

**Deliberately left out, to keep all four demos identical:**

- **Integrated vectorization.** Azure AI Search can call your embedding model itself, at indexing and query time. Here the C# app calls it, so the comparison stays fair.
- **Indexers.** In production, an indexer often pulls data from Cosmos DB or Azure SQL. Here the app pushes documents, like the other demos import them.
- **Semantic ranker.** It re-ranks the top results with a language model, and it isn't needed for this comparison.

## Part 2: Setup

### Step 1: Check the prerequisites

| You need | Notes |
| --- | --- |
| .NET 11 SDK | Run `dotnet --version`. |
| An Azure AI Search service | The **Free** tier works for this demo. Each subscription can have one free service. |
| Foundry deployments of `text-embedding-3-small` and `gpt-6-luna` | The same deployments as the other demos. |

### Step 2: Create a search service

Skip this step if you already have a service.

1. In the [Azure portal](https://portal.azure.com), select **Create a resource**, then search for **Azure AI Search** and select **Create**.
2. Choose your subscription and a resource group.
3. Enter a **Service name**. It becomes part of the endpoint, `https://SERVICE-NAME.search.windows.net`.
4. Choose a **Location**. Pick the same region as your Foundry resource if you can.
5. Under **Pricing tier**, select **Change Pricing Tier**, then **Free**.
6. Select **Review + create**, then **Create**.

**Free tier limits:** 50 MB of storage, 3 indexes, and shared hardware. The 12 products and their vectors use a small fraction of that.

### Step 3: Choose how the app signs in

**Option A: an admin key.** This is simplest for a demo.

1. Open your search service in the portal.
2. Go to **Settings** > **Keys**, and copy the **Primary admin key**.
3. You'll store it as `Search:ApiKey` in the next step.

**Option B: Microsoft Entra ID.** No key is stored anywhere.

1. Open your search service, go to **Settings** > **Keys**, and select **Role-based access control** or **Both**.
2. Go to **Access control (IAM)** and give your account two roles:
   - **Search Service Contributor**, to create and delete the index.
   - **Search Index Data Contributor**, to upload and search documents.
3. Role changes can take a few minutes to apply. Then run this:

```bash
az login
```

### Step 4: Store your settings

Your Azure OpenAI settings are already stored, because this project shares user secrets with the other three demos. Add the search service settings by running these in this folder:

```bash
dotnet user-secrets set "Search:Endpoint" "https://YOUR-SERVICE.search.windows.net"
```

For option A only:

```bash
dotnet user-secrets set "Search:ApiKey" "YOUR-ADMIN-KEY"
```

Optional settings, with their defaults:

| Setting | Default |
| --- | --- |
| `Search:IndexName` | `products` |
| `Search:ApiKey` | Not set, so the app signs in as your `az login` account |

### Step 5: Run the app

```bash
dotnet run
```

## Part 3: Running the demo

The steps are numbered the same as in the other demos. Press **Enter** at any prompt to accept the suggested text. **Before the demo starts**, choose `R` if you've run it earlier.

### 1. Show the source JSON

**What happens:** The app lists the 12 products and prints one full description.

**Say:** "Same file again, and this time it goes into a search engine instead of a database."

### 2. Create the index with a vector field

**What happens:** The app creates the `products` index. Show this in [Demo.cs](Demo.cs):

```csharp
new VectorSearchField("embedding", 1536, "vector-profile"),

VectorSearch = new VectorSearch
{
    Profiles = { new VectorSearchProfile("vector-profile", "hnsw-config") },
    Algorithms = { new HnswAlgorithmConfiguration("hnsw-config")
        { Parameters = new HnswParameters { Metric = VectorSearchAlgorithmMetric.Cosine } } },
},
```

**Say:** "An index is a schema of fields, like a table. The vector field points to a profile, and the profile says HNSW with cosine distance."

**Show in the portal:** your search service > **Search management** > **Indexes** > `products` > **Fields**, and **Vector profiles**.

### 3. Import the documents

**What happens:** All 12 documents are uploaded in one batch. Upload replaces whole documents, so re-importing clears any vectors, just like the other demos.

**Show in the portal:** open the index and select **Search** in **Search explorer**. The documents have no `embedding` values yet.

### 4. Keyword search: the "before" picture

**What happens:** Press Enter to search for `something to keep my feet dry on a rainy walk`. The app removes the filler words and asks BM25 for documents that contain every remaining word. It should find nothing.

**Say:** "This is a real search engine, with ranking and word breaking, and it still can't help when the customer's words and our words differ."

### 5. Vectorize the documents

**What happens:** The app reads the documents back, sends their text to `text-embedding-3-small` in one batch, and **merges** each vector into its document. Merge only changes the fields you send, so the other fields stay as they are.

**Say:** "New vectors can take a moment to become searchable. A search index is eventually consistent, unlike a database transaction."

### 6. Inspect a vector

**What happens:** The app reads one document back with its vector, and prints the start of it.

**Good to know:** the vector is stored twice, once in the HNSW index and once so it can be returned. Microsoft's docs say that making it non-retrievable can cut its storage by up to 50%. The demo keeps it retrievable so that this step works.

### 7. Compare keyword, vector and hybrid search

**What happens:** The app runs three searches on the same text. The first two match the other demos. The third is Azure AI Search's own addition.

| Search | What it does | Scores |
| --- | --- | --- |
| Keyword (BM25) | Every keyword must appear | BM25 relevance |
| Vector | Nearest documents by meaning | Cosine similarity, the same scale as the other demos |
| Hybrid | Keyword and vector in parallel, merged with RRF | Small RRF values: only their order matters |

**About the vector scores:** Azure AI Search reports cosine results as `1 / (1 + distance)`, which runs from about 0.333 to 1. The app converts this back to cosine similarity, using `similarity = 2 - 1 / score`, so all four demos show the same numbers for the same match.

**Say:** "Hybrid is why people pick a search service. It catches exact product names and part numbers, where vectors are weak, and meaning, where keywords are weak, in one request."

### 8. Vector search with a price filter

**What happens:** Press Enter twice to search for `a shelter for two people` at $300 or less. The filter is an OData expression, `price le 300`, applied **before** the vector search, so you still get 4 results under budget.

### 9. Ask the agent about the data

**What happens:** The agent code is the same as in the other demos, apart from the query inside `search_catalog`. Use the same questions:

| Question | What it proves |
| --- | --- |
| Which tent should I buy for a stormy mountain trip, and what is its warranty? | Real product and warranty details come from your data. |
| How much does that one weigh? | The agent remembers the conversation. |
| What is the best shelter for two if my budget is $300? | The agent should pass the budget to the tool as `maxPrice`. |
| I'm planning a two-week winter trek. Which sleeping bag and pack do you recommend, and what do they cost? | The answer combines several documents. |
| Can your water filter make water safe from viruses? | It finds a caveat buried in a description. |
| Do you sell surfboards? | It should say no instead of inventing a product. |

## Part 4: All four compared

| Topic | Cosmos DB | PostgreSQL | SQL Server 2025 | Azure AI Search |
| --- | --- | --- | --- | --- |
| What it is | Database | Database | Database | Search service |
| Enabling vectors | Account feature | `pgvector` extension | Built in | Built in |
| Vector index in the demo | DiskANN | HNSW | None, exact search | HNSW |
| Keyword ranking | Basic in the demo | Basic in the demo | Basic in the demo | BM25 |
| Hybrid search in one request | Not shown | Not shown | Not shown | Yes, RRF |
| Transactions with your data | Within a partition | Yes | Yes | No: a separate copy |
| Freshness after a write | Immediate | Immediate | Immediate | Eventually consistent |
| Agent code | Identical | Identical | Identical | Identical |

**A fair summary:** if your data lives in a database, start with that database's vectors. Add Azure AI Search when search relevance across many sources matters more than having one copy of the data.

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| `Missing setting 'Search:Endpoint'` | Complete setup step 4 from this folder. |
| `Search:Endpoint must start with https://` | Use the full URL, for example `https://YOUR-SERVICE.search.windows.net`. |
| `Azure AI Search refused access` | Check `Search:ApiKey`, or the two roles from setup step 3, and that the service allows role-based access. |
| `The index doesn't exist yet` | Run demo step 2 first. |
| `AzureCliCredential authentication failed` | Run `az login`, or set `Search:ApiKey`. |
| Vector search returns nothing straight after step 5 | Wait a few seconds and try again: new documents take a moment to become searchable. |
| Vector search returns no documents | Run step 5. Running step 3 again clears the vectors, so run step 5 after any re-import. |
| An index quota or storage error | The Free tier allows 3 indexes and 50 MB. Delete unused indexes. |
| 401, 403 or 404 from Azure OpenAI | See the troubleshooting table in the [Cosmos DB README](../MAF-CosmosDB-Demo/README.md#troubleshooting). |

## Cleaning up

Choose `R` to delete the index. If you created a service just for this demo, delete it in the portal when you're done. A Free service costs nothing, but each subscription can only have one.

## Project files

| File | Purpose |
| --- | --- |
| [data/products.json](data/products.json) | An exact copy of the data used by the other demos |
| [Program.cs](Program.cs) | Menu and sample questions |
| [Demo.cs](Demo.cs) | The search logic, the agents and the `search_catalog` tool, one method per step |
| [Product.cs](Product.cs) | Product model and the text sent for embedding |
| [Settings.cs](Settings.cs) | Reads user secrets and environment variables |
| [Ui.cs](Ui.cs) | Console formatting helpers |

## Sources

- [Vector search overview](https://learn.microsoft.com/en-us/azure/search/vector-search-overview)
- [Vector relevance and ranking](https://learn.microsoft.com/en-us/azure/search/vector-search-ranking)
- [Hybrid search overview](https://learn.microsoft.com/en-us/azure/search/hybrid-search-overview)
- [Vector compression and storage](https://learn.microsoft.com/en-us/azure/search/vector-search-how-to-configure-compression-storage)
- [Service limits](https://learn.microsoft.com/en-us/azure/search/search-limits-quotas-capacity)
