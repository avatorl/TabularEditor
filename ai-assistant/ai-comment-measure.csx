using Newtonsoft.Json.Linq;
using System.Net.Http;
using System.Threading.Tasks;

// Execute the function to interact with the DAX assistant and comment on the selected measure
await UseDaxAssistantAsync();

async Task UseDaxAssistantAsync()
{
    // CONFIGURATION SECTION
    string apiKey = "OPEN_AI_API_KEY";
    string assistantId = "ASSISTANT_ID"; // Replace with your Assistant ID
    string baseUrl = "https://api.openai.com/v1";
    // END OF CONFIGURATION

    // Step 1: Get the selected measure from Tabular Editor
    var selectedMeasure = Selected.Measures.FirstOrDefault();

    if (selectedMeasure == null)
    {
        Error("No measure is selected. Please select a measure.");
        return;
    }

    // Extract the measure's name and DAX expression
    string measureExpression = selectedMeasure.Expression;

    // Define the user's query for the assistant
    string userQuery = $"Please add comments to the following measure. Make sure first 1-2 rows are comments describing the entire measure and also add comment into the following code, explaining important code rows. The result must be a valid DAX measure expression, as would be used directly inside a measure definition. Do not use any characters to identify code start and end (e.g, ```DAX and ```), output only DAX code. Only output the DAX code (without any quote marks outside of the measure code). Ensure the output DAX code excludes the measure name and equal sign (=), containing only the commented measure logic and no other comments outside of the measure code. Consider the entire data model (refer to the provided DataModel.json file for context).\n\nMeasure Expression: {measureExpression}";

    using (var client = new HttpClient())
    {
        // Add the authorization header with the API key
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        client.DefaultRequestHeaders.Add("OpenAI-Beta", "assistants=v2");

        // Step 2: Create a new thread for the conversation
        var threadResponse = await client.PostAsync($"{baseUrl}/threads", null);
        if (!threadResponse.IsSuccessStatusCode)
        {
            await LogApiError(threadResponse);
            return;
        }

        var threadContent = await threadResponse.Content.ReadAsStringAsync();
        var threadResult = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(threadContent);
        string threadId = threadResult.id.ToString();

        // Step 3: Create a user message and add it to the thread
        var messagePayload = new
        {
            role = "user",
            content = userQuery
        };

        var messageRequestBody = new StringContent(
            Newtonsoft.Json.JsonConvert.SerializeObject(messagePayload),
            System.Text.Encoding.UTF8,
            "application/json"
        );

        var messageResponse = await client.PostAsync($"{baseUrl}/threads/{threadId}/messages", messageRequestBody);
        if (!messageResponse.IsSuccessStatusCode)
        {
            await LogApiError(messageResponse);
            return;
        }

        // Step 4: Run the assistant using the thread and assistant ID
        var runPayload = new
        {
            assistant_id = assistantId
        };

        var runRequestBody = new StringContent(
            Newtonsoft.Json.JsonConvert.SerializeObject(runPayload),
            System.Text.Encoding.UTF8,
            "application/json"
        );

        var runResponse = await client.PostAsync($"{baseUrl}/threads/{threadId}/runs", runRequestBody);
        if (!runResponse.IsSuccessStatusCode)
        {
            await LogApiError(runResponse);
            return;
        }

        // Step 5: Wait for the assistant to complete and retrieve all messages in the thread
        // Directly retrieve the messages once the run is completed
        var messagesResponse = await client.GetAsync($"{baseUrl}/threads/{threadId}/messages");
        if (!messagesResponse.IsSuccessStatusCode)
        {
            await LogApiError(messagesResponse);
            return;
        }

        var messagesContent = await messagesResponse.Content.ReadAsStringAsync();
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
                updatedDaxExpression = value.Replace("\\n", System.Environment.NewLine);

                // Break after the first assistant message
                break;
            }
        }

        if (string.IsNullOrEmpty(updatedDaxExpression))
        {
            Error("Failed to retrieve a valid DAX expression from the assistant.");
            return;
        }

        // Step 6: Create a new measure with the AI-generated DAX expression

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
async Task LogApiError(HttpResponseMessage response)
{
    var errorContent = await response.Content.ReadAsStringAsync();

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
