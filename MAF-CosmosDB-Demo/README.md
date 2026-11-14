# Azure Cosmos DB vectorization demo (C#)

A menu-driven .NET console app that shows, one step at a time, what vectorization adds to Azure Cosmos DB for NoSQL. It imports an outdoor gear catalog with long product descriptions, shows keyword search failing on natural questions, and vectorizes the documents with `text-embedding-3-small`. A Microsoft Agent Framework 1.28.0 agent running on `gpt-6-luna` then answers questions by searching the data itself. Both models are your deployments in Azure OpenAI, through your Foundry resource.

**The story for your audience:** the data doesn't move to a separate vector database. The vectors live on the same documents, next to the business data, and are queried with ordinary Cosmos DB SQL.

![Azure Cosmos DB demo flow: products.json is imported into the Cosmos DB container in steps 1 to 3, and text-embedding-3-small adds a vector to each document in step 5. In steps 7 and 8, search text is embedded and searched with VectorDistance, which returns the top 4 documents. In step 9, a question goes to the Agent Framework agent, which calls the search_catalog tool to embed it and run VectorDistance, gets the top 4 documents back, and returns a grounded answer.](media/cosmosdb-flow.png)

## Part 1: Setup

### Step 1: Check the prerequisites

| You need | Notes |
| --- | --- |
| .NET 11 SDK | Run `dotnet --version`. |
| Azure Cosmos DB for **NoSQL** account | Vector search here uses the NoSQL API. A MongoDB or other API account won't work. |
| Vector search enabled on the account | Portal: your account > **Settings** > **Features** > **Vector Search for NoSQL API**. It can take around 15 minutes to activate. |
| Foundry resource with two model deployments | One for `text-embedding-3-small` and one for `gpt-6-luna`. The defaults assume each deployment has the same name as its model. |
| Azure CLI | Only if you sign in with Microsoft Entra ID, the default. |

### Step 2: Collect your Cosmos DB values

1. Open your Cosmos DB account in the [Azure portal](https://portal.azure.com).
2. Go to **Settings** > **Keys**.
3. Copy the **URI** and the **PRIMARY KEY**.

The app creates the database and container itself, so you don't need to create them in the portal. If your account has key authentication disabled, this demo won't connect, because it signs in with the key.

**Then collect your Foundry values:**

1. Open your project in the [Foundry portal](https://ai.azure.com).
2. On the project overview page, copy the **Azure OpenAI endpoint**, for example `https://YOUR-RESOURCE.openai.azure.com/`. The app only uses the host name, so a project URL on that resource also works.
3. Open your model deployments and note the **deployment names**, which may differ from the model names.

**Then give yourself access to the models.** Entra ID is the default sign-in, and it needs the **Cognitive Services OpenAI User** role on the Foundry resource. Role changes can take a few minutes to apply. Azure subscription owners don't automatically get this role.

```bash
az login
```

To sign in with a key instead, skip the role and the login, and set the optional `AzureOpenAI:ApiKey` setting from step 3.

### Step 3: Store your settings

Run these commands in this folder. User secrets are stored in your Windows profile, not in the project, so your keys can't end up in source control or on a slide.

```bash
dotnet user-secrets set "Cosmos:Endpoint" "https://YOUR-ACCOUNT.documents.azure.com:443/"
```

```bash
dotnet user-secrets set "Cosmos:Key" "YOUR-PRIMARY-KEY"
```

```bash
dotnet user-secrets set "AzureOpenAI:Endpoint" "https://YOUR-RESOURCE.openai.azure.com/"
```

If your deployment names differ from the model names, set them too:

```bash
dotnet user-secrets set "AzureOpenAI:EmbeddingDeployment" "YOUR-EMBEDDING-DEPLOYMENT"
```

```bash
dotnet user-secrets set "AzureOpenAI:ChatDeployment" "YOUR-CHAT-DEPLOYMENT"
```

Optional settings, with their defaults:

| Setting | Default |
| --- | --- |
| `Cosmos:Database` | `VectorDemo` |
| `Cosmos:Container` | `Products` |
| `AzureOpenAI:EmbeddingDeployment` | `text-embedding-3-small` |
| `AzureOpenAI:ChatDeployment` | `gpt-6-luna` |
| `AzureOpenAI:ApiKey` | Not set, so the app signs in as your `az login` account |

The embedding deployment must produce 1536-dimension vectors to match the container, which is the default for `text-embedding-3-small`.

### Step 4: Run the app

```bash
dotnet run
```

A menu appears. At every prompt you can press **Enter** to accept a suggested question, so you don't have to type during the demo.

## Part 2: Running the demo

Run the steps in order. **Before the demo starts**, choose `R` if you've run it earlier, so that the audience sees a clean start.

### 1. Show the source JSON

**What happens:** The app lists the 12 products in [data/products.json](data/products.json) and prints one full description.

**Say:** "This is ordinary JSON, with a paragraph of about 100 words for each product. There's nothing about AI in here."

### 2. Create the container with a vector policy

**What happens:** The app creates the `VectorDemo` database and the `Products` container, partitioned on `/category`.

**Say:** "Two settings make this container vector-aware." Then show them in [Demo.cs](Demo.cs):

| Setting | Value | Why |
| --- | --- | --- |
| Vector path | `/embedding` | The property that will hold the vector. |
| Dimensions | `1536` | Must match what `text-embedding-3-small` produces. |
| Data type | `Float32` | The format of each number. |
| Distance function | `Cosine` | How closeness in meaning is measured. |
| Vector index | `DiskANN` | Stays fast at very large scale. |
| Excluded path | `/embedding/*` | Keeps the raw numbers out of the regular index, which saves RUs on writes. |

**Good to know:** The `flat` index tops out at 505 dimensions, so it can't hold these 1536-dimension vectors. `DiskANN` and `quantizedFlat` go up to 4,096. Both run as a full scan below 1,000 vectors, which is fine for 12 documents, but it means **this demo doesn't show DiskANN's speed advantage**. Don't claim that it does.

**Show in the portal:** Data Explorer > `VectorDemo` > `Products` > **Settings** > **Container Policies** > **Vector Policy**.

### 3. Import the documents

**What happens:** Each JSON document is upserted to Cosmos DB, and the RU cost is printed.

**Show in the portal:** Open a document in Data Explorer and point out that there's no `embedding` property yet.

### 4. Keyword search: the "before" picture

**What happens:** Press Enter to search for `something to keep my feet dry on a rainy walk`. The query removes filler words and uses `CONTAINS` to require every remaining word. It finds nothing.

**Say:** "We sell a boot made for exactly this, but its description says *sodden terrain*, *puddles* and *downpours*. The customer's words and our words don't match."

### 5. Vectorize the documents

This is the centre of the demo. The app prints each stage:

- **a)** It reads the documents from Cosmos DB.
- **b)** It sends each document's name, category, description, features and warranty to `text-embedding-3-small`, in one batch. The text is built by `Product.ToEmbeddingText()` in [Product.cs](Product.cs).
- **c)** It writes each returned vector back onto its own document.

**Show in the portal:** Refresh the document from step 3. The original fields are unchanged, and there's a new `embedding` array of 1,536 numbers.

### 6. Inspect a vector

**What happens:** The app prints the first numbers of one vector.

**Say:** "No single number means anything on its own. Together they are coordinates for the meaning of the text, so similar meanings end up close together."

### 7. Compare keyword search and vector search

**What happens:** The app runs both searches on the same text and prints the results one after the other. This is the main "aha" moment. Press Enter to move through the samples each time you return to this step:

| Sample search | Expected vector match | What it proves |
| --- | --- | --- |
| `I get cold at night when camping in winter` | Polaris sleeping bag | Matches meaning: the text says *sub-zero nights*. |
| `how do I make a hot meal far from any town` | Ember stove | Works from a need, not a product name. |
| `necesito algo para purificar agua en la montaña` | ClearStream filter | A Spanish search finds English documents. |
| `how can I call for help when I have no phone signal` | SafeGuide messenger | Understands the whole intent. |

All five sample searches, including the one from step 4, return no keyword results against this data. Expected vector matches are predictions until you've done your rehearsal run.

The vector query is ordinary Cosmos DB SQL:

```sql
SELECT TOP 4 c.id, c.name, c.price, VectorDistance(c.embedding, @vector) AS score
FROM c
WHERE IS_DEFINED(c.embedding)
ORDER BY VectorDistance(c.embedding, @vector)
```

The question has to be embedded by the same model as the documents, because vectors from different models can't be compared.

### 8. Vector search with a price filter

**What happens:** Press Enter twice to search for `a shelter for two people` at $300 or less. The $649 Fjord tent is filtered out, and the $279 Wisp tent stays.

**Say:** "This is the benefit of keeping vectors with your data. Meaning and a business rule run in one query, with no second system to keep in sync."

### 9. Ask the agent about the data

**What happens:** This is retrieval-augmented generation (RAG), built as a Microsoft Agent Framework agent. The agent is created in the `Demo` constructor in [Demo.cs](Demo.cs), with instructions and one tool, `search_catalog`.

1. A plain agent on the same deployment, with no tools, answers first, to show what a generic answer looks like.
2. The `ProductAdvisor` agent receives the question and decides to call `search_catalog`. The app prints each call in yellow, including the search text the agent chose.
3. The tool embeds that text and runs the same `VectorDistance` query as step 7.
4. Agent Framework returns the products to the model, which answers only from them.

**Say:** "We didn't write the search into a prompt. We gave the agent a tool, and it decides what to search for and whether a budget applies. Cosmos DB is now the agent's knowledge."

The conversation carries across questions through an `AgentSession`. Choose `N` to clear it. Press Enter to move through these questions:

| Question | What it proves |
| --- | --- |
| Which tent should I buy for a stormy mountain trip, and what is its warranty? | Real product and warranty details come from your data. |
| How much does that one weigh? | The agent remembers the conversation, so "that one" makes sense. |
| What is the best shelter for two if my budget is $300? | The agent should pass the budget to the tool as `maxPrice` by itself. |
| I'm planning a two-week winter trek. Which sleeping bag and pack do you recommend, and what do they cost? | The answer combines several documents. |
| Can your water filter make water safe from viruses? | It finds a caveat buried in a description: the filter doesn't remove viruses. |
| Do you sell surfboards? | With nothing relevant in the catalog, it should say so instead of inventing a product. |

## Part 3: Summary of the benefits

| Benefit | Where it was shown |
| --- | --- |
| Search by meaning, not exact words | Steps 4 and 7 |
| Search across languages | Step 7, Spanish sample |
| One database for data and vectors, with nothing extra to sync | Steps 5 and 8 |
| Vector and business filters in one SQL query | Step 8 |
| Grounded AI answers using your own data | Step 9 |
| Vector search as a tool an agent can choose to use | Step 9 |

**If someone asks whether the keyword comparison is fair:** it's deliberately simple. Searching for a word that does appear, such as `boot`, works fine. Requiring every word is also strict, and Cosmos DB has full-text and hybrid search that would do better than `CONTAINS`. Vector search earns its place when the user's words differ from the document's words, which is the case the demo shows.

**Vector search always returns something.** It gives the nearest documents even when none is relevant, as you'll see with the surfboard question. That's why the agent's instructions tell it to refuse when the results don't answer the question. A production app would normally also apply a minimum score.

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| `Missing setting 'Cosmos:Endpoint'` | Repeat setup step 3 from this folder. |
| 400 error mentioning vector policies when creating the container | Vector search isn't active yet. Check the account's **Features** page and allow time for activation. |
| `already exists without a vector policy` | Choose `R`, then run step 2 again, or set a different `Cosmos:Container`. |
| `Unauthorized` from Cosmos DB | Copy the key again. Also check that key authentication is enabled on the account. |
| `AzureCliCredential authentication failed` | Run `az login`. If the Foundry resource is in another tenant, run `az login --tenant YOUR-TENANT-ID`. |
| 401 or 403 from Azure OpenAI | Check that you have the **Cognitive Services OpenAI User** role on the Foundry resource, or check `AzureOpenAI:ApiKey`. New roles can take a few minutes to apply. |
| 404 or `DeploymentNotFound` from Azure OpenAI | The deployment name doesn't match. Set `AzureOpenAI:EmbeddingDeployment` or `AzureOpenAI:ChatDeployment` to the exact name. |
| The agent says it can't search, and a red `search_catalog failed` line appears | The red line has the real cause. The usual one is that step 5 hasn't been run or that the embedding deployment can't be reached. |
| 429 from Cosmos DB | The container is out of RU/s. Raise throughput or wait a moment. |
| Connection blocked by a firewall | Add your IP under **Networking** on the Cosmos DB account. The app uses HTTPS on port 443 only. |
| Vector search returns no results | Run step 5. Running step 3 again replaces documents **without** their vectors, so run step 5 after any re-import. |

## Cleaning up

Choose `R` to delete the container. If your database has provisioned throughput or you've finished with the demo, also delete the `VectorDemo` database in Data Explorer so that it stops costing RU/s.

## Project files

| File | Purpose |
| --- | --- |
| [data/products.json](data/products.json) | The 12 fictional products to import |
| [Program.cs](Program.cs) | Menu and sample questions |
| [Demo.cs](Demo.cs) | The Cosmos DB logic, the agents and the `search_catalog` tool, one method per step |
| [Product.cs](Product.cs) | Document model and the text sent for embedding |
| [Settings.cs](Settings.cs) | Reads user secrets and environment variables |
| [Ui.cs](Ui.cs) | Console formatting helpers |

## How it connects to Azure OpenAI

- **Agent Framework:** `Microsoft.Agents.AI.OpenAI` 1.28.0 turns a Responses client into an `AIAgent` with `AsAIAgent`.
- **Connection:** the app uses the OpenAI SDK with the Azure OpenAI v1 endpoint, `https://YOUR-RESOURCE.openai.azure.com/openai/v1/`. This means `Azure.AI.OpenAI` isn't needed.
- **Responses API:** the agent uses it because `gpt-6-luna` only supports function calling on Chat Completions when reasoning is turned off.
- **Evaluation APIs:** the OpenAI SDK marks the Responses client and token sign-in as evaluation APIs, so [CosmosVectorDemo.csproj](CosmosVectorDemo.csproj) suppresses `OPENAI001`. Pin the package versions if you reuse this code.
