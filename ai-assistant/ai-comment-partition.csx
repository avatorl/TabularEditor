using Newtonsoft.Json.Linq;
using Microsoft.Win32;
using System.Net.Http;

// This script interacts with an OpenAI assistant using an assistant ID.
// It sends a selected M query for commenting, retrieves the AI-generated comments,
// and updates the selected partition with the AI's M query.

UseDaxAssistant(); // Initiates the process of interacting with the AI assistant.

void UseDaxAssistant()
{
    // --- CONFIGURATION SECTION ---
    // OpenAI API key and assistant ID, either provided directly or fetched from environment variables

    string apiKeyInput = "";  // Your OpenAI API key (leave blank to use environment variable)
    string apiKey = string.Empty;  // Final API key storage

    // Retrieve API key from environment variables if not provided directly
    if (string.IsNullOrEmpty(apiKeyInput))
    {
        using (RegistryKey userKey = Registry.CurrentUser.OpenSubKey(@"Environment"))
        {
            if (userKey != null)
            {
                apiKey = userKey.GetValue("OPENAI_TE_API_KEY") as string;
            }
        }
    }
    else
    {
        apiKey = apiKeyInput; // Use the provided API key if available
    }

    string assistantIdInput = "";  // The assistant ID (leave blank to use environment variable)
    string assistantId = string.Empty;  // Final assistant ID storage

    // Retrieve assistant ID from environment variables if not provided directly
    if (string.IsNullOrEmpty(assistantIdInput))
    {
        using (RegistryKey userKey = Registry.CurrentUser.OpenSubKey(@"Environment"))
        {
            if (userKey != null)
            {
                assistantId = userKey.GetValue("OPENAI_TE_ASSISTANT_ID") as string;
            }
        }
    }
    else
    {
        assistantId = assistantIdInput; // Use the provided assistant ID if available
    }

    string baseUrl = "https://api.openai.com/v1";  // Base API URL for OpenAI
    int maxAttempts = 10;  // Maximum number of attempts to poll the run status

    // --- END OF CONFIGURATION ---

    // Retrieve the M query from the selected partition (if one exists)
    var mCode = "";
    if (Selected.Partition != null)
    {
        mCode = Selected.Partition.Expression;
    }

    // Step 3: Define the user's query for the assistant
    string userQuery = $"Please add comments to the following M query. Use '//' to define comment row. " +
                       $"Make sure first 1-5 rows are comments briefly describing the entire query and also " +
                       $"add comment into the following M code, explaining all important code rows. " +
                       $"Make sure a comment related to a certain code row is placed right before (not after) this row. " +
                       $"The result must be a valid M query, as would be used directly inside a query. " +
                       $"Do not use any characters to define code block start and end (such as M or ``` or json), " +
                       $"output only M code. Only output the M code (without any quote marks outside of the M code). " +
                       $"Mandatory output format: {{\"query\":\"<put M query code here>\"}}. " +
                       $"The output must start from {{ and end with }}\n\nQuery Code: {mCode}";

    // Step 4: Create an HttpClient instance for making API requests
    using (var client = new HttpClient())
    {
        // Add authorization header with the API key
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        client.DefaultRequestHeaders.Add("OpenAI-Beta", "assistants=v2");  // Beta feature for assistants

        // Step 5: Get the vector store associated with the assistant
        var vectorStoreId = GetVectorStoreForAssistant(client, baseUrl, assistantId);
        if (vectorStoreId == null)
        {
            Error("No vector store available to use.");
            return;  // Exit if no vector store found
        }

        // Step 6: Create a new thread for the conversation with the vector store attached
        var threadPayload = new
        {
            tool_resources = new
            {
                file_search = new
                {
                    vector_store_ids = new[] { vectorStoreId }  // Attach vector store
                }
            }
        };

        var threadRequestBody = new StringContent(
            JsonConvert.SerializeObject(threadPayload),  // Serialize the payload to JSON
            System.Text.Encoding.UTF8,
            "application/json"
        );

        // Send request to create a new thread
        var threadResponse = client.PostAsync($"{baseUrl}/threads", threadRequestBody).Result;
        if (!threadResponse.IsSuccessStatusCode)
        {
            LogApiError(threadResponse);
            return;  // Exit on API error
        }

        // Step 7: Parse the response to get the thread ID
        var threadContent = threadResponse.Content.ReadAsStringAsync().Result;
        var threadResult = JsonConvert.DeserializeObject<dynamic>(threadContent);
        string threadId = threadResult.id.ToString();  // Extract thread ID from response

        // Step 8: Create a user message and add it to the thread
        var messagePayload = new
        {
            role = "user",  // User role for the assistant
            content = userQuery  // The M query we want the assistant to comment on
        };

        var messageRequestBody = new StringContent(
            JsonConvert.SerializeObject(messagePayload),
            System.Text.Encoding.UTF8,
            "application/json"
        );

        // Send the message to the assistant
        var messageResponse = client.PostAsync($"{baseUrl}/threads/{threadId}/messages", messageRequestBody).Result;
        if (!messageResponse.IsSuccessStatusCode)
        {
            LogApiError(messageResponse);
            return;  // Exit on API error
        }

        // Step 9: Run the assistant using the thread and assistant ID
        var runPayload = new { assistant_id = assistantId };
        var runRequestBody = new StringContent(
            JsonConvert.SerializeObject(runPayload),
            System.Text.Encoding.UTF8,
            "application/json"
        );

        var runResponse = client.PostAsync($"{baseUrl}/threads/{threadId}/runs", runRequestBody).Result;
        if (!runResponse.IsSuccessStatusCode)
        {
            LogApiError(runResponse);
            return;  // Exit on API error
        }

        var runResult = JsonConvert.DeserializeObject<dynamic>(runResponse.Content.ReadAsStringAsync().Result);
        string runId = runResult.id.ToString();  // Extract run ID
        string runStatus = runResult.status.ToString();  // Get run status

        // Step 10: Poll the run status until it's completed or failed
        int attempts = 1;
        while (runStatus != "completed" && runStatus != "failed" && attempts < maxAttempts)
        {
            attempts++;
            System.Threading.Thread.Sleep(3000);  // Wait 3 seconds before polling again

            // Check the status of the assistant's run
            var runStatusResponse = client.GetAsync($"{baseUrl}/threads/{threadId}/runs/{runId}").Result;

            if (!runStatusResponse.IsSuccessStatusCode)
            {
                LogApiError(runStatusResponse);
                return;  // Exit on API error
            }

            // Update run status
            var runStatusResult = JsonConvert.DeserializeObject<dynamic>(runStatusResponse.Content.ReadAsStringAsync().Result);
            runStatus = runStatusResult.status.ToString();
        }

        // Step 11: Check if the assistant run failed or timed out
        if (runStatus == "failed" || attempts >= maxAttempts)
        {
            Error("Operation failed or timed out.");
            return;  // Exit if the assistant failed or timed out
        }

        // Step 12: Retrieve the messages from the thread to get the assistant's response
        var messagesResponse = client.GetAsync($"{baseUrl}/threads/{threadId}/messages").Result;
        if (!messagesResponse.IsSuccessStatusCode)
        {
            LogApiError(messagesResponse);
            return;  // Exit on API error
        }

        var messagesResult = JsonConvert.DeserializeObject<dynamic>(messagesResponse.Content.ReadAsStringAsync().Result);
        string updatedMQuery = string.Empty;

        // Step 13: Extract the AI-generated commented M query from the assistant's message
        foreach (var msg in messagesResult.data)
        {
            if (msg.role.ToString() == "assistant")
            {
                JArray jsonArray = JArray.Parse(msg.content.ToString());
                string value = (string)jsonArray[0]["text"]["value"];
                updatedMQuery = value.Replace("\\n", Environment.NewLine);  // Replace escaped newlines
                break;  // Process the first assistant message only
            }
        }

        // Step 14: If the AI didn't return a valid M query, exit
        if (string.IsNullOrEmpty(updatedMQuery))
        {
            Error("Failed to retrieve a valid M query.");
            return;
        }

        // Output the updated M query
        //Output(updatedMQuery);

        // Parse the JSON and update the partition expression with the new commented M query
        JObject parsedJson = JObject.Parse(updatedMQuery);
        Selected.Partition.Expression = parsedJson["query"].ToString();
    }
}

// Function to log API errors
void LogApiError(HttpResponseMessage response)
{
    var errorContent = response.Content.ReadAsStringAsync().Result;
    try
    {
        var errorResult = JsonConvert.DeserializeObject<dynamic>(errorContent);
        var errorMessage = errorResult.error?.message?.ToString();
        Error($"API call failed. Status: {response.StatusCode}, Message: {errorMessage}");
    }
    catch (Exception ex)
    {
        Error($"API call failed. Status: {response.StatusCode}, Error Content: {errorContent}, Exception: {ex.Message}");
    }
}

// Function to get the vector store attached to the assistant
string GetVectorStoreForAssistant(HttpClient client, string baseUrl, string assistantId)
{
    // Retrieve the assistant's configuration
    var response = client.GetAsync($"{baseUrl}/assistants/{assistantId}").Result;

    if (!response.IsSuccessStatusCode)
    {
        var errorContent = response.Content.ReadAsStringAsync().Result;
        Error($"Failed to retrieve assistant configuration: {errorContent}");
        return null;
    }

    var assistantData = JsonConvert.DeserializeObject<dynamic>(response.Content.ReadAsStringAsync().Result);

    // Check if vector_store_ids are present in the assistant's tool_resources
    if (assistantData.tool_resources?.file_search?.vector_store_ids != null)
    {
        return assistantData.tool_resources.file_search.vector_store_ids[0]?.ToString();  // Return the first vector store ID
    }

    Error("No vector store found attached to this assistant.");
    return null;
}
