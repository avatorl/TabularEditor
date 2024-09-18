// Import necessary namespaces
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using System.Threading.Tasks;
using System.Linq;
using Newtonsoft.Json.Linq;
using System;

// Function to interact with the OpenAI assistant using the assistant ID
public async Task UseDaxAssistantAsync()
{
    // Replace with your OpenAI API key
    string apiKey = "OPEN_AI_API_KEY";
    string assistantId = "ASSISTANT_ID";  // Replace this with the actual Assistant ID you obtained earlier
    string baseUrl = "https://api.openai.com/v1";

    // Step 1: Get the selected measure from Tabular Editor
    var selectedMeasure = Selected.Measures.FirstOrDefault();
    
    if (selectedMeasure == null)
    {
        Error("No measure is selected. Please select a measure.");
        return;
    }

    // Define the selected measure's DAX expression and the query to the assistant
    string measureName = selectedMeasure.Name;
    string measureExpression = selectedMeasure.Expression;

    // Define the user's query, asking the assistant to comment on the measure
    string userQuery = $"Please add comments to the following measure. The result must be a valid DAX measure expression, as would be used directly inside a measure definition. Do not use any characters to identify code start and end (e.g, ```DAX and ```), output only DAX code. Only output the DAX code (without any qoute qoute marks outside of the measure code). \n\nMeasure Expression: {measureExpression}\n\nConsider the entire data model to understand the measure.";

    // Create HttpClient instance
    using (var client = new HttpClient())
    {
        // Add the authorization header with the API key
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        // Add the required OpenAI-Beta header
        client.DefaultRequestHeaders.Add("OpenAI-Beta", "assistants=v2");

        // Step 2: Create a new thread for the conversation
        var threadResponse = await client.PostAsync($"{baseUrl}/threads", null);

        if (!threadResponse.IsSuccessStatusCode)
        {
            var errorContent = await threadResponse.Content.ReadAsStringAsync();
            Error($"Thread creation failed: {errorContent}");
            return;
        }

        var threadResult = JsonConvert.DeserializeObject<dynamic>(await threadResponse.Content.ReadAsStringAsync());
        string threadId = threadResult.id.ToString();

        // Step 3: Create a user message and add it to the thread
        var messagePayload = new
        {
            role = "user",
            content = userQuery
        };

        var messageRequestBody = new StringContent(JsonConvert.SerializeObject(messagePayload), Encoding.UTF8, "application/json");
        var messageResponse = await client.PostAsync($"{baseUrl}/threads/{threadId}/messages", messageRequestBody);

        if (!messageResponse.IsSuccessStatusCode)
        {
            var errorContent = await messageResponse.Content.ReadAsStringAsync();
            Error($"Message creation failed: {errorContent}");
            return;
        }

        // Step 4: Run the assistant using the thread and assistant ID
        var runPayload = new
        {
            assistant_id = assistantId
        };

        var runRequestBody = new StringContent(JsonConvert.SerializeObject(runPayload), Encoding.UTF8, "application/json");
        var runResponse = await client.PostAsync($"{baseUrl}/threads/{threadId}/runs", runRequestBody);

        if (!runResponse.IsSuccessStatusCode)
        {
            var errorContent = await runResponse.Content.ReadAsStringAsync();
            Error($"Assistant run failed: {errorContent}");
            return;
        }

        var runResult = JsonConvert.DeserializeObject<dynamic>(await runResponse.Content.ReadAsStringAsync());
        string runId = runResult.id.ToString();
        string runStatus = runResult.status.ToString();

        // Step 5: Poll the run status until it's completed
        while (runStatus != "completed")
        {
            var runStatusResponse = await client.GetAsync($"{baseUrl}/threads/{threadId}/runs/{runId}");

            if (!runStatusResponse.IsSuccessStatusCode)
            {
                var errorContent = await runStatusResponse.Content.ReadAsStringAsync();
                Error($"Run status retrieval failed: {errorContent}");
                return;
            }

            var runStatusResult = JsonConvert.DeserializeObject<dynamic>(await runStatusResponse.Content.ReadAsStringAsync());
            runStatus = runStatusResult.status.ToString();

            if (runStatus == "completed")
            {
                break;
            }

            // Poll every 5 seconds
            await Task.Delay(5000);
        }

        // Step 6: Retrieve all messages in the thread (including the assistant's response)
        var messagesResponse = await client.GetAsync($"{baseUrl}/threads/{threadId}/messages");

        if (!messagesResponse.IsSuccessStatusCode)
        {
            var errorContent = await messagesResponse.Content.ReadAsStringAsync();
            Error($"Failed to retrieve messages: {errorContent}");
            return;
        }

        var messagesResult = JsonConvert.DeserializeObject<dynamic>(await messagesResponse.Content.ReadAsStringAsync());

        // Extract the AI-generated DAX expression from the assistant's response
        string updatedDaxExpression = string.Empty;

        var i = 0;

        foreach (var msg in messagesResult.data)
        {
            string role = msg.role.ToString();
            string content = msg.content.ToString();

            if (role == "assistant")
            {

                if (i==0) {

                       // Parse the JSON string into a JArray
                    JArray jsonArray = JArray.Parse(content);

                    // Extract the "value" field
                    string value = (string)jsonArray[0]["text"]["value"];

                    // Replace \n with actual new lines
                    string validDax = value.Replace("\\n", Environment.NewLine);
                    updatedDaxExpression = validDax;
                    break; 
                }

                i=i+1;
            }
        }

        if (string.IsNullOrEmpty(updatedDaxExpression))
        {
            Error("Failed to retrieve a valid DAX expression from the assistant.");
            return;
        }

        // Step 7: Create a new measure with the AI-generated DAX expression

        // Define a new measure name by appending "_Copy" to the original measure name
        string newMeasureName = selectedMeasure.Name + "_Copy_with_AI_comments";

        // Check if a measure with the new name already exists in the model
        var existingMeasure = selectedMeasure.Table.Measures.FirstOrDefault(m => m.Name == newMeasureName);
        if (existingMeasure != null)
        {
            Error($"A measure named '{newMeasureName}' already exists. Choose a different name.");
            return;
        }

        // Create a copy of the selected measure and update the expression with AI-generated DAX code
        var newMeasure = selectedMeasure.Table.AddMeasure(newMeasureName, updatedDaxExpression);

        // Format the DAX expression of the new measure (optional)
        newMeasure.FormatDax();
    }
}

// Execute the function to interact with the DAX assistant and comment on the selected measure
await UseDaxAssistantAsync();
