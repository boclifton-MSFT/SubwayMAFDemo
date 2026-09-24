# Subway Store Assistant demo

A small .NET 10 and TypeScript web app that demonstrates Microsoft Agent Framework with a Foundry model and two local function tools:

- `menu_lookup` searches an illustrative sandwich catalog.
- `estimate_order` calculates an illustrative subtotal.

The prices and sample catalog are invented for this demo. They are not Subway's official menu, prices, ingredient data, or allergen guidance.

## Configure and run

Prerequisites: .NET 10 SDK, Node.js 20.19+ or 22.12+, an Azure AI Foundry project with a deployed chat model, and permission to use that project.

In PowerShell, sign in and set the project endpoint and model deployment name. Use your Foundry project's endpoint and the name of its deployed model:

```powershell
az login
$env:FOUNDRY_PROJECT_ENDPOINT = "https://<resource>.services.ai.azure.com/api/projects/<project>"
$env:FOUNDRY_MODEL = "<your-model-deployment-name>"
```

For quick local UI development, open two PowerShell terminals in the project folder.

Terminal 1 — start the .NET API:

```powershell
dotnet run
```

Terminal 2 — install and start the TypeScript UI:

```powershell
npm --prefix client install
npm --prefix client run dev
```

Open the Vite URL printed in Terminal 2 (usually `http://127.0.0.1:5173`). The development server forwards `/api` requests to the .NET app at `http://localhost:5080`.

To serve the built UI from the .NET app instead:

```powershell
npm --prefix client install
npm --prefix client run build
dotnet run
```

Then open `http://localhost:5080`.

The app uses `DefaultAzureCredential`, which can use the Azure CLI sign-in for local development. `FOUNDRY_MODEL` defaults to `gpt-5.4-mini` if omitted. The `AZURE_OPENAI_ENDPOINT` and `AZURE_OPENAI_DEPLOYMENT_NAME` environment-variable names are also accepted. Without an endpoint, the web UI still loads and shows setup guidance, but chat requests require a configured Foundry project.

## Suggested 2-minute demo

1. Click **Explore the menu** in the left panel to see the assistant use `menu_lookup`.
2. Click **Estimate an order** to run `estimate_order` and calculate a subtotal.
3. Ask `What if I only wanted one?` to show that the chat remembers context within the conversation.
4. Click **Ask about ingredients** to show the assistant's safety boundary: it does not claim to verify ingredients or allergens and refers people to current official information or store staff.

## What to point out

The model handles natural-language conversation and decides when to call tools. The C# tool functions own the menu lookup and arithmetic, so the assistant is instructed not to invent menu items or prices. The TypeScript UI keeps a separate conversation session per browser tab and the .NET API keeps session state in memory. Start a new chat to reset the conversation. Replace the demo catalog and pricing with approved source data before using this beyond a presentation.
