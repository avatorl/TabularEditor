using Newtonsoft.Json.Linq;

// This script interacts with an OpenAI assistant using an assistant ID.
// It sends a selected DAX measure for commenting, retrieves the AI-generated comments,
// and creates a new backup measure with the AI's DAX expression.

UseDaxAssistant(); // Initiates the process of interacting with the AI assistant.

void UseDaxAssistant()
{
    // --- CONFIGURATION SECTION ---
    string apiKey = "OPEN_AI_API_KEY"; // Your OpenAI API key
    string assistantId = "asst_h4nZvnYmh0qzbkBxYITattvc"; // The assistant ID you want to use
    string baseUrl = "https://api.openai.com/v1"; // Base API URL
    int maxAttempts = 10; // Maximum number of attempts to poll the run status
    // --- END OF CONFIGURATION ---

    // Step 1: Check if a measure is selected
    var selectedMeasure = Selected.Measures.FirstOrDefault();
    if (selectedMeasure == null)
    {
        Error("No measure is selected. Please select a measure.");
        return; // Exit if no measure is selected
    }

    // Step 2: Extract the measure's name and DAX expression
    string measureName = selectedMeasure.Name;
    string measureExpression = selectedMeasure.Expression;

    // Step 3: Define the user's query for the assistant
    string userQuery = $"Please add comments to the following measure. Use '//' to define comment row. Make sure first 1-5 rows are comments briefly describing the entire measure (measure description) and also add comment into the following DAX code, explaining all important code rows. The result must be a valid DAX measure expression, as would be used directly inside a measure definition. Do not use any characters to define code block start and end (such as DAX or ``` or json), output only DAX code. Only output the DAX code (without any qoute marks outside of the measure code). Ensure the output DAX code excludes the measure name and equal sign (=), containing only the commented measure logic and no other comments outside of the measure code. Consider the entire data model (refer to the provided DataModel.json file) for context. Verify that you only added or edited existing comments, without changing the original DAX code (other than adding comments). Mandatory output format: {{\"expression\":\"<put measure DAX expression here>\",\"description\":\"<put measure description here>\"}}. There should be no characters beyond the JSON. The output must start from {{ and end with }}\n\nMeasure Expression: {measureExpression}";

    // Step 4: Create an HttpClient instance for making API requests
    using (var client = new System.Net.Http.HttpClient())
    {
        // Add authorization header with the API key
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        client.DefaultRequestHeaders.Add("OpenAI-Beta", "assistants=v2");

        // Step 5: Create a new thread for the conversation
        var threadResponse = client.PostAsync($"{baseUrl}/threads", null).Result;
        if (!threadResponse.IsSuccessStatusCode)
        {
            LogApiError(threadResponse);
            return; // Exit on API error
        }

        // Step 6: Parse the response to get the thread ID
        var threadContent = threadResponse.Content.ReadAsStringAsync().Result;
        var threadResult = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(threadContent);
        string threadId = threadResult.id.ToString(); // Extract thread ID

        // Step 7: Create a user message and add it to the thread
        var messagePayload = new
        {
            role = "user",
            content = userQuery
        };

        var messageRequestBody = new System.Net.Http.StringContent(
            Newtonsoft.Json.JsonConvert.SerializeObject(messagePayload),
            System.Text.Encoding.UTF8,
            "application/json"
        );

        var messageResponse = client.PostAsync($"{baseUrl}/threads/{threadId}/messages", messageRequestBody).Result;
        if (!messageResponse.IsSuccessStatusCode)
        {
            LogApiError(messageResponse);
            return;
        }

        // Step 8: Run the assistant using the thread and assistant ID
        var runPayload = new { assistant_id = assistantId };
        var runRequestBody = new System.Net.Http.StringContent(
            Newtonsoft.Json.JsonConvert.SerializeObject(runPayload),
            System.Text.Encoding.UTF8,
            "application/json"
        );

        var runResponse = client.PostAsync($"{baseUrl}/threads/{threadId}/runs", runRequestBody).Result;
        if (!runResponse.IsSuccessStatusCode)
        {
            LogApiError(runResponse);
            return;
        }

        var runResult = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(runResponse.Content.ReadAsStringAsync().Result);
        string runId = runResult.id.ToString();
        string runStatus = runResult.status.ToString();

        // Step 9: Poll the run status until it's completed or failed
        int attempts = 1;
        while (runStatus != "completed" && runStatus != "failed" && attempts < maxAttempts)
        {
            attempts++;
            System.Threading.Thread.Sleep(3000); // Wait 3 seconds before polling again
            var runStatusResponse = client.GetAsync($"{baseUrl}/threads/{threadId}/runs/{runId}").Result;

            if (!runStatusResponse.IsSuccessStatusCode)
            {
                LogApiError(runStatusResponse);
                return;
            }

            var runStatusResult = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(runStatusResponse.Content.ReadAsStringAsync().Result);
            runStatus = runStatusResult.status.ToString();
        }

        // Step 10: Check if the assistant run failed or timed out
        if (runStatus == "failed" || attempts >= maxAttempts)
        {
            Error("Operation failed or timed out.");
            return;
        }

        // Step 11: Retrieve the messages from the thread to get the assistant's response
        var messagesResponse = client.GetAsync($"{baseUrl}/threads/{threadId}/messages").Result;
        if (!messagesResponse.IsSuccessStatusCode)
        {
            LogApiError(messagesResponse);
            return;
        }

        var messagesResult = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(messagesResponse.Content.ReadAsStringAsync().Result);
        string updatedDaxExpression = string.Empty;

        // Step 12: Extract the AI-generated DAX expression from the assistant's message
        foreach (var msg in messagesResult.data)
        {
            if (msg.role.ToString() == "assistant")
            {
                JArray jsonArray = JArray.Parse(msg.content.ToString());
                string value = (string)jsonArray[0]["text"]["value"];
                updatedDaxExpression = value.Replace("\\n", Environment.NewLine); // Handle newlines
                break; // Exit after processing the first assistant message
            }
        }

        // If the AI didn't return a valid DAX expression, exit
        if (string.IsNullOrEmpty(updatedDaxExpression))
        {
            Error("Failed to retrieve a valid DAX expression.");
            return;
        }

        // Step 13: Create a backup of the original measure
        Guid newGuid = Guid.NewGuid(); // Generate a unique identifier
        string backupMeasureName = $"{selectedMeasure.Name}_backup_{newGuid}";

        var newMeasure = selectedMeasure.Table.AddMeasure(backupMeasureName, measureExpression);
        newMeasure.DisplayFolder = selectedMeasure.DisplayFolder;
        newMeasure.Description = selectedMeasure.Description;
        newMeasure.FormatString = selectedMeasure.FormatString;

        // Step 14: Update the selected measure with the AI-generated DAX code and description
try
{
    JObject parsedJson = JObject.Parse(updatedDaxExpression);
    selectedMeasure.Expression = parsedJson["expression"].ToString();
    selectedMeasure.Description = parsedJson["description"].ToString();
    selectedMeasure.FormatDax(); // Format the measure for better readability
}
catch
{
    Output(updatedDaxExpression);
}
        
    }
}

// Function to log API errors
void LogApiError(System.Net.Http.HttpResponseMessage response)
{
    var errorContent = response.Content.ReadAsStringAsync().Result;
    try
    {
        var errorResult = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(errorContent);
        var errorMessage = errorResult.error?.message?.ToString();
        Error($"API call failed. Status: {response.StatusCode}, Message: {errorMessage}");
    }
    catch (Exception ex)
    {
        Error($"API call failed. Status: {response.StatusCode}, Error Content: {errorContent}, Exception: {ex.Message}");
    }
}
