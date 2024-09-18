// This script interacts with the OpenAI assistant using the assistant ID.
// It sends a selected DAX measure to the assistant for commenting, and creates a new measure with the AI-generated DAX code.

// Execute the function to interact with the DAX assistant and comment on the selected measure
UseDaxAssistant();

void UseDaxAssistant()
{
    // CONFIGURATION SECTION
    //To create an API key read https://help.openai.com/en/articles/9186755-managing-your-work-in-the-api-platform-with-projects
    string apiKey = "OPEN_AI_API_KEY";
    string assistantId = "ASSISTANT_ID"; // Replace with your Assistant ID
    string baseUrl = "https://api.openai.com/v1";   
    // also define userQuery
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
    string userQuery = $"Please add comments to the following measure. The result must be a valid DAX measure expression, as would be used directly inside a measure definition. Do not use any characters to identify code start and end (e.g., do not use ```DAX and ``` delimiters), output only DAX code. Only output the DAX code (without any quote marks outside of the measure code).\n\nMeasure Expression: {measureExpression}\n\nConsider the entire data model (use attached DataModel.json to understand the measure.";

    // Create an HttpClient instance for making API requests
    using (var client = new System.Net.Http.HttpClient())
    {
        // Add the authorization header with the API key
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        // Add the required OpenAI-Beta header
        client.DefaultRequestHeaders.Add("OpenAI-Beta", "assistants=v2");

        // Step 2: Create a new thread for the conversation
        var threadResponse = client.PostAsync($"{baseUrl}/threads", null).Result;

        if (!threadResponse.IsSuccessStatusCode)
        {
            var errorContent = threadResponse.Content.ReadAsStringAsync().Result;
            Error($"Thread creation failed: {errorContent}");
            return;
        }

        // Deserialize the response to get the thread ID
        var threadResult = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(threadResponse.Content.ReadAsStringAsync().Result);
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

        var messageResponse = client.PostAsync($"{baseUrl}/threads/{threadId}/messages", messageRequestBody).Result;

        if (!messageResponse.IsSuccessStatusCode)
        {
            var errorContent = messageResponse.Content.ReadAsStringAsync().Result;
            Error($"Message creation failed: {errorContent}");
            return;
        }

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

        var runResponse = client.PostAsync($"{baseUrl}/threads/{threadId}/runs", runRequestBody).Result;

        if (!runResponse.IsSuccessStatusCode)
        {
            var errorContent = runResponse.Content.ReadAsStringAsync().Result;
            Error($"Assistant run failed: {errorContent}");
            return;
        }

        // Deserialize the run response to get the run ID and status
        var runResult = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(runResponse.Content.ReadAsStringAsync().Result);
        string runId = runResult.id.ToString();
        string runStatus = runResult.status.ToString();

        // Step 5: Poll the run status until it's completed
        while (runStatus != "completed")
        {
            var runStatusResponse = client.GetAsync($"{baseUrl}/threads/{threadId}/runs/{runId}").Result;

            if (!runStatusResponse.IsSuccessStatusCode)
            {
                var errorContent = runStatusResponse.Content.ReadAsStringAsync().Result;
                Error($"Run status retrieval failed: {errorContent}");
                return;
            }

            var runStatusResult = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(runStatusResponse.Content.ReadAsStringAsync().Result);
            runStatus = runStatusResult.status.ToString();

            if (runStatus == "completed")
            {
                break;
            }

            // Wait for 5 seconds before polling again
            System.Threading.Thread.Sleep(5000);
        }

        // Step 6: Retrieve all messages in the thread, including the assistant's response
        var messagesResponse = client.GetAsync($"{baseUrl}/threads/{threadId}/messages").Result;

        if (!messagesResponse.IsSuccessStatusCode)
        {
            var errorContent = messagesResponse.Content.ReadAsStringAsync().Result;
            Error($"Failed to retrieve messages: {errorContent}");
            return;
        }

        var messagesResult = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(messagesResponse.Content.ReadAsStringAsync().Result);

        // Extract the AI-generated DAX expression from the assistant's response
        string updatedDaxExpression = string.Empty;

        foreach (var msg in messagesResult.data)
        {
            string role = msg.role.ToString();
            string content = msg.content.ToString();

            if (role == "assistant")
            {
                // Parse the JSON string into a JArray
                var jsonArray = Newtonsoft.Json.Linq.JArray.Parse(content);

                // Extract the "value" field containing the DAX code
                string value = (string)jsonArray[0]["text"]["value"];

                // Replace escaped new line characters with actual new lines
                string validDax = value.Replace("\\n", System.Environment.NewLine);
                updatedDaxExpression = validDax;
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
