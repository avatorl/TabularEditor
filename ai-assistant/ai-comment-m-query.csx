using Newtonsoft.Json.Linq;
using Microsoft.Win32;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading;

// This script interacts with an OpenAI assistant to add comments to the M code
// of the selected partition in Tabular Editor.
// It retrieves the M code, sends it to the assistant for commenting,
// and updates the partition with the AI-generated commented M code.

// Initiate the process
UseMCodeAssistant();

void UseMCodeAssistant()
{
    // --- CONFIGURATION SECTION ---
    
    // OpenAI API key. Leave blank to use the environment variable.
    string apiKeyInput = ""; 
    string apiKey = GetEnvironmentVariableOrInput(apiKeyInput, "OPENAI_TE_API_KEY");

    // Assistant ID. Leave blank to use the environment variable.
    string assistantIdInput = ""; 
    string assistantId = GetEnvironmentVariableOrInput(assistantIdInput, "OPENAI_TE_ASSISTANT_ID");

    // Base URL for OpenAI API
    string baseUrl = "https://api.openai.com/v1"; 

    // Maximum number of attempts to poll the run status
    int maxAttempts = 10; 
    // --- END OF CONFIGURATION ---

    // Step 1: Retrieve the M code from the selected partition
    string mCode = GetSelectedPartitionMCode();
    if (string.IsNullOrEmpty(mCode))
    {
        Error("No partition is selected or the selected partition does not contain M code.");
        return;
    }

    // Step 2: Define the user's query for the assistant to add comments to the M code
    string userQuery = GenerateUserQuery(mCode);

    // Step 3: Create an HttpClient instance for making API requests
    using (var client = new HttpClient())
    {
        // Add authorization header with the API key
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        client.DefaultRequestHeaders.Add("OpenAI-Beta", "assistants=v2");

        // Step 4: Get the vector store associated with the assistant
        var vectorStoreId = GetVectorStoreForAssistant(client, baseUrl, assistantId);
        if (vectorStoreId == null)
        {
            Error("No vector store available to use.");
            return;
        }

        // Step 5: Create a new thread for the conversation with the vector store attached
        string threadId = CreateThread(client, baseUrl, vectorStoreId);
        if (string.IsNullOrEmpty(threadId))
        {
            Error("Failed to create a thread with the assistant.");
            return;
        }

        // Step 6: Send the user query to the assistant
        bool messageSent = SendMessageToThread(client, baseUrl, threadId, userQuery);
        if (!messageSent)
        {
            Error("Failed to send the message to the assistant.");
            return;
        }

        // Step 7: Run the assistant using the thread and assistant ID
        string runId = StartAssistantRun(client, baseUrl, threadId, assistantId);
        if (string.IsNullOrEmpty(runId))
        {
            Error("Failed to start the assistant run.");
            return;
        }

        // Step 8: Poll the run status until it's completed or failed
        string runStatus = PollRunStatus(client, baseUrl, threadId, runId, maxAttempts);
        if (runStatus != "completed")
        {
            Error("Operation failed or timed out.");
            return;
        }

        // Step 9: Retrieve the assistant's response
        string updatedMQuery = GetAssistantResponse(client, baseUrl, threadId);
        if (string.IsNullOrEmpty(updatedMQuery))
        {
            Error("Failed to retrieve a valid M query from the assistant.");
            return;
        }

        // Step 10: Update the partition with the AI-generated commented M code
        UpdatePartitionMCode(updatedMQuery);
    }
}

#region Helper Functions

/// <summary>
/// Retrieves the M code from the selected partition.
/// </summary>
/// <returns>The M code as a string, or null if not available.</returns>
string GetSelectedPartitionMCode()
{
    if (Selected.Partition != null)
    {
        return Selected.Partition.Expression;
    }
    return null;
}

/// <summary>
/// Generates the user query to be sent to the assistant for adding comments to the M code.
/// </summary>
/// <param name="mCode">The original M code.</param>
/// <returns>A formatted query string.</returns>
string GenerateUserQuery(string mCode)
{
    return $"Please add comments to the following M query. Use '//' to define comment rows. " +
           $"Ensure the first 1-5 rows are comments briefly describing the entire query, " +
           $"and add comments to explain all important code lines. The result must be a valid M query " +
           $"suitable for direct use in a query. Do not use any characters to define code block " +
           $"start and end (such as M or ``` or json). Output only the M code without any quote marks " +
           $"outside of the M code. Consider the entire data model (refer to the provided DataModel.json file) " +
           $"for context. Verify that you only added or edited existing comments without changing the original " +
           $"M code (other than adding comments). " +
           $"Mandatory output format: {{\"query\":\"<put M query code here>\"}}. " +
           $"There should be no characters beyond the JSON. The output must start with {{ and end with }}.\n\n" +
           $"Query Code: {mCode}";
}

/// <summary>
/// Retrieves the value from an environment variable or uses the provided input.
/// </summary>
/// <param name="input">The direct input value.</param>
/// <param name="envVariableName">The name of the environment variable.</param>
/// <returns>The retrieved value.</returns>
string GetEnvironmentVariableOrInput(string input, string envVariableName)
{
    if (string.IsNullOrEmpty(input))
    {
        using (RegistryKey userKey = Registry.CurrentUser.OpenSubKey(@"Environment"))
        {
            if (userKey != null)
            {
                return userKey.GetValue(envVariableName) as string;
            }
        }
    }
    else
    {
        return input;
    }
    return string.Empty;
}

/// <summary>
/// Retrieves the vector store ID associated with the assistant.
/// </summary>
/// <param name="client">The HttpClient instance.</param>
/// <param name="baseUrl">The base URL for the API.</param>
/// <param name="assistantId">The assistant ID.</param>
/// <returns>The vector store ID, or null if not found.</returns>
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
        return assistantData.tool_resources.file_search.vector_store_ids[0]?.ToString();
    }

    Error("No vector store found attached to this assistant.");
    return null;
}

/// <summary>
/// Creates a new thread with the specified vector store.
/// </summary>
/// <param name="client">The HttpClient instance.</param>
/// <param name="baseUrl">The base URL for the API.</param>
/// <param name="vectorStoreId">The vector store ID.</param>
/// <returns>The thread ID, or null if creation failed.</returns>
string CreateThread(HttpClient client, string baseUrl, string vectorStoreId)
{
    var threadPayload = new
    {
        tool_resources = new
        {
            file_search = new
            {
                vector_store_ids = new[] { vectorStoreId }
            }
        }
    };

    var threadRequestBody = new StringContent(
        JsonConvert.SerializeObject(threadPayload),
        System.Text.Encoding.UTF8,
        "application/json"
    );

    var threadResponse = client.PostAsync($"{baseUrl}/threads", threadRequestBody).Result;
    if (!threadResponse.IsSuccessStatusCode)
    {
        LogApiError(threadResponse);
        return null;
    }

    var threadContent = threadResponse.Content.ReadAsStringAsync().Result;
    var threadResult = JsonConvert.DeserializeObject<dynamic>(threadContent);
    return threadResult.id.ToString();
}

/// <summary>
/// Sends a message to the specified thread.
/// </summary>
/// <param name="client">The HttpClient instance.</param>
/// <param name="baseUrl">The base URL for the API.</param>
/// <param name="threadId">The thread ID.</param>
/// <param name="message">The message content.</param>
/// <returns>True if the message was sent successfully; otherwise, false.</returns>
bool SendMessageToThread(HttpClient client, string baseUrl, string threadId, string message)
{
    var messagePayload = new
    {
        role = "user",
        content = message
    };

    var messageRequestBody = new StringContent(
        JsonConvert.SerializeObject(messagePayload),
        System.Text.Encoding.UTF8,
        "application/json"
    );

    var messageResponse = client.PostAsync($"{baseUrl}/threads/{threadId}/messages", messageRequestBody).Result;
    if (!messageResponse.IsSuccessStatusCode)
    {
        LogApiError(messageResponse);
        return false;
    }
    return true;
}

/// <summary>
/// Starts the assistant run for the specified thread and assistant ID.
/// </summary>
/// <param name="client">The HttpClient instance.</param>
/// <param name="baseUrl">The base URL for the API.</param>
/// <param name="threadId">The thread ID.</param>
/// <param name="assistantId">The assistant ID.</param>
/// <returns>The run ID, or null if the run could not be started.</returns>
string StartAssistantRun(HttpClient client, string baseUrl, string threadId, string assistantId)
{
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
        return null;
    }

    var runResult = JsonConvert.DeserializeObject<dynamic>(runResponse.Content.ReadAsStringAsync().Result);
    return runResult.id.ToString();
}

/// <summary>
/// Polls the run status until it is completed or failed, or until the maximum number of attempts is reached.
/// </summary>
/// <param name="client">The HttpClient instance.</param>
/// <param name="baseUrl">The base URL for the API.</param>
/// <param name="threadId">The thread ID.</param>
/// <param name="runId">The run ID.</param>
/// <param name="maxAttempts">The maximum number of polling attempts.</param>
/// <returns>The final run status.</returns>
string PollRunStatus(HttpClient client, string baseUrl, string threadId, string runId, int maxAttempts)
{
    string runStatus = "";
    int attempts = 0;

    while (attempts < maxAttempts)
    {
        var runStatusResponse = client.GetAsync($"{baseUrl}/threads/{threadId}/runs/{runId}").Result;
        if (!runStatusResponse.IsSuccessStatusCode)
        {
            LogApiError(runStatusResponse);
            break;
        }

        var runStatusResult = JsonConvert.DeserializeObject<dynamic>(runStatusResponse.Content.ReadAsStringAsync().Result);
        runStatus = runStatusResult.status.ToString();

        if (runStatus == "completed" || runStatus == "failed")
        {
            break;
        }

        attempts++;
        Thread.Sleep(3000); // Wait 3 seconds before polling again
    }

    return runStatus;
}

/// <summary>
/// Retrieves the assistant's response from the specified thread.
/// </summary>
/// <param name="client">The HttpClient instance.</param>
/// <param name="baseUrl">The base URL for the API.</param>
/// <param name="threadId">The thread ID.</param>
/// <returns>The assistant's updated M query, or null if not found.</returns>
string GetAssistantResponse(HttpClient client, string baseUrl, string threadId)
{
    var messagesResponse = client.GetAsync($"{baseUrl}/threads/{threadId}/messages").Result;
    if (!messagesResponse.IsSuccessStatusCode)
    {
        LogApiError(messagesResponse);
        return null;
    }

    var messagesResult = JsonConvert.DeserializeObject<dynamic>(messagesResponse.Content.ReadAsStringAsync().Result);
    foreach (var msg in messagesResult.data)
    {
        if (msg.role.ToString() == "assistant")
        {
            // Parse the JSON response to extract the updated M query
            JObject jsonArray = JArray.Parse(msg.content.ToString());
            string value = (string)jsonArray[0]["text"]["value"];
            return value.Replace("\\n", Environment.NewLine); // Handle newlines
        }
    }

    return null;
}

/// <summary>
/// Updates the selected partition with the AI-generated commented M code.
/// </summary>
/// <param name="updatedMQuery">The updated M query with comments.</param>
void UpdatePartitionMCode(string updatedMQuery)
{
    try
    {
        // Parse the JSON to extract the "query" field
        JObject parsedJson = JObject.Parse(updatedMQuery);
        string commentedMCode = parsedJson["query"]?.ToString();

        if (!string.IsNullOrEmpty(commentedMCode))
        {
            Selected.Partition.Expression = commentedMCode;
            Output("Partition M code has been successfully updated with comments.");
        }
        else
        {
            Error("The 'query' field is missing in the assistant's response.");
        }
    }
    catch (Exception ex)
    {
        Error($"Failed to parse the assistant's response: {ex.Message}");
    }
}

/// <summary>
/// Logs API errors by extracting and displaying error messages.
/// </summary>
/// <param name="response">The HTTP response containing the error.</param>
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

#endregion
