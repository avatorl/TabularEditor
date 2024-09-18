// This script interacts with the OpenAI assistant using the assistant ID.
// It sends a selected DAX measure to the assistant for commenting and creates a new measure with the AI-generated DAX code.

using Newtonsoft.Json.Linq;

// Execute the function to interact with the DAX assistant and comment on the selected measure
UseDaxAssistant();

void UseDaxAssistant()
{
    // CONFIGURATION SECTION
    string apiKey = "OPEN_AI_API_KEY";
    string assistantId = "ASSISTANT_ID"; // Replace with your Assistant ID
    string baseUrl = "https://api.openai.com/v1";
    int maxAttempts = 10;
    // END OF CONFIGURATION

    // Step 1: Get the selected measure from Tabular Editor
    var selectedMeasure = Selected.Measures.FirstOrDefault();

    if (selectedMeasure == null)
    {
        Error("No measure is selected. Please select a measure.");
        return;
    }

    // Extract the measure's name and DAX expression
    string measureName = selectedMeasure.Name;
    string measureExpression = selectedMeasure.Expression;

    // Define the user's query for the assistant
    string userQuery = $"Please add comments to the following measure. Make sure first 1-2 rows are comments describing the entire measure and also add comment into the following code, explaining important code rows. The result must be a valid DAX measure expression, as would be used directly inside a measure definition. Do not use any characters to identify code start and end (e.g, ```DAX and ```), output only DAX code. Only output the DAX code (without any qoute qoute marks outside of the measure code). Ensure the output DAX code excludes the measure name and equal sign (=), containing only the commented measure logic and no other comments outside of the measure code. Consider the entire data model (refer to the provided DataModel.json file for context).\n\nMeasure Expression: {measureExpression}";

    // Create an HttpClient instance for making API requests
    using (var client = new System.Net.Http.HttpClient())
    {
        // Add the authorization header with the API key
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        // Add the required OpenAI-Beta header if needed
        client.DefaultRequestHeaders.Add("OpenAI-Beta", "assistants=v2");

        // Step 2: Create a new thread for the conversation
        //Info($"Creating a new thread at {baseUrl}/threads");
        var threadResponse = client.PostAsync($"{baseUrl}/threads", null).Result;

        if (!threadResponse.IsSuccessStatusCode)
        {
            LogApiError(threadResponse);
            return;
        }

        var threadContent = threadResponse.Content.ReadAsStringAsync().Result;
        //Info($"Thread Response: {threadContent}");

        // Deserialize the response to get the thread ID
        var threadResult = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(threadContent);
        string threadId = threadResult.id.ToString();

        // Step 3: Create a user message and add it to the thread
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

        //Info($"Sending user message to {baseUrl}/threads/{threadId}/messages");
        var messageResponse = client.PostAsync($"{baseUrl}/threads/{threadId}/messages", messageRequestBody).Result;

        if (!messageResponse.IsSuccessStatusCode)
        {
            LogApiError(messageResponse);
            return;
        }

        var messageContent = messageResponse.Content.ReadAsStringAsync().Result;
        //Info($"Message Response: {messageContent}");

        // Step 4: Run the assistant using the thread and assistant ID
        var runPayload = new
        {
            assistant_id = assistantId
        };

        var runRequestBody = new System.Net.Http.StringContent(
            Newtonsoft.Json.JsonConvert.SerializeObject(runPayload),
            System.Text.Encoding.UTF8,
            "application/json"
        );

        //Info($"Starting assistant run at {baseUrl}/threads/{threadId}/runs");
        var runResponse = client.PostAsync($"{baseUrl}/threads/{threadId}/runs", runRequestBody).Result;

        if (!runResponse.IsSuccessStatusCode)
        {
            LogApiError(runResponse);
            return;
        }

        var runContent = runResponse.Content.ReadAsStringAsync().Result;
        //Info($"Run Response: {runContent}");

        // Deserialize the run response to get the run ID and status
        var runResult = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(runContent);
        string runId = runResult.id.ToString();
        string runStatus = runResult.status.ToString();

        // Step 5: Poll the run status until it's succeeded or failed
        
        int attempts = 1;

        while (runStatus != "completed" && runStatus != "failed" && attempts < maxAttempts)
        {

            attempts++;

            // Wait for 3 seconds before polling
            System.Threading.Thread.Sleep(3000);

            //Info($"Polling run status at {baseUrl}/threads/{threadId}/runs/{runId}");
            var runStatusResponse = client.GetAsync($"{baseUrl}/threads/{threadId}/runs/{runId}").Result;

            if (!runStatusResponse.IsSuccessStatusCode)
            {
                LogApiError(runStatusResponse);
                return;
            }

            var runStatusContent = runStatusResponse.Content.ReadAsStringAsync().Result;
            //Info($"Run Status Response: {runStatusContent}");

            var runStatusResult = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(runStatusContent);
            runStatus = runStatusResult.status.ToString();

            // Log the current run status
            //Info($"Attempt {attempts}: Current run status: {runStatus}");
        }

        if (runStatus == "failed")
        {
            // Retrieve error details from the run status response
            var runStatusResponse = client.GetAsync($"{baseUrl}/threads/{threadId}/runs/{runId}").Result;
            var runStatusContent = runStatusResponse.Content.ReadAsStringAsync().Result;
            var runStatusResult = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(runStatusContent);
            var errorMessage = runStatusResult.error?.message?.ToString();
            Error($"Assistant run failed. Error message: {errorMessage}");
            return;
        }

        if (attempts >= maxAttempts)
        {
            Error("Operation timed out after maximum attempts.");
            return;
        }

        // Step 6: Retrieve all messages in the thread, including the assistant's response
        //Info($"Retrieving messages from {baseUrl}/threads/{threadId}/messages");
        var messagesResponse = client.GetAsync($"{baseUrl}/threads/{threadId}/messages").Result;

        if (!messagesResponse.IsSuccessStatusCode)
        {
            LogApiError(messagesResponse);
            return;
        }

        var messagesContent = messagesResponse.Content.ReadAsStringAsync().Result;
        //Info($"Messages Response: {messagesContent}");

        var messagesResult = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(messagesContent);

        // Extract the AI-generated DAX expression from the assistant's response
        string updatedDaxExpression = string.Empty;

        foreach (var msg in messagesResult.data)
        {
            if (msg.role.ToString() == "assistant")
            {
                // Parse the JSON string into a JArray
                JArray jsonArray = JArray.Parse(msg.content.ToString());

                // Extract the "value" field
                string value = (string)jsonArray[0]["text"]["value"];

                // Replace \n with actual new lines
                updatedDaxExpression = value.Replace("\\n", Environment.NewLine);

                // Break after the first assistant message
                break;
            }
        }       

        if (string.IsNullOrEmpty(updatedDaxExpression))
        {
            Error("Failed to retrieve a valid DAX expression from the assistant.");
            return;
        }

        // Step 7: Create a new measure with the AI-generated DAX expression

        // Define a new measure name by appending a suffix to the original measure name
        string newMeasureName = selectedMeasure.Name + "_Copy_with_AI_comments";

        // Check if a measure with the new name already exists in the model
        var existingMeasure = selectedMeasure.Table.Measures.FirstOrDefault(m => m.Name == newMeasureName);
        if (existingMeasure != null)
        {
            Error($"A measure named '{newMeasureName}' already exists. Choose a different name.");
            return;
        }

        // Create a new measure with the AI-generated DAX expression
        var newMeasure = selectedMeasure.Table.AddMeasure(newMeasureName, updatedDaxExpression);

        // Copy the Display Folder from the original measure to the new measure
        newMeasure.DisplayFolder = selectedMeasure.DisplayFolder;

        // Format the DAX expression of the new measure (optional)
        newMeasure.FormatDax();

        // Inform the user that the new measure has been created
        Info($"New measure '{newMeasureName}' created with AI-generated comments.");
    }
}

// Method to log API errors
void LogApiError(System.Net.Http.HttpResponseMessage response)
{
    var errorContent = response.Content.ReadAsStringAsync().Result;

    // Try to deserialize the error content
    try
    {
        var errorResult = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(errorContent);
        var errorMessage = errorResult.error?.message?.ToString();
        Error($"API call failed. Status Code: {response.StatusCode}, Error Message: {errorMessage}");
    }
    catch (Exception ex)
    {
        // If deserialization fails, log the raw error content
        Error($"API call failed. Status Code: {response.StatusCode}, Error Content: {errorContent}, Exception: {ex.Message}");
    }
}
