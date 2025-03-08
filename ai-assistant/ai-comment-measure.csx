using Newtonsoft.Json.Linq;
using Microsoft.Win32;
using System.Linq; // Added for LINQ support

// This script interacts with an OpenAI assistant using an assistant ID.
// It sends a selected DAX measure for commenting, retrieves the AI-generated comments,
// and creates a new backup measure with the AI's DAX expression.

UseDaxAssistant(); // Initiates the process of interacting with the AI assistant.

void UseDaxAssistant()
{
    // --- CONFIGURATION SECTION ---

    string baseUrl = "https://api.openai.com/v1"; // Base API URL
    int maxAttempts = 30; // Maximum number of attempts to poll the run status
    double temperature = 0.4;
    string model = "gpt-4o-mini";
    string apiKeyInput = ""; // Your OpenAI API key, or leave blank to use environment variable "OPENAI_TE_API_KEY"

    // --- END OF CONFIGURATION ---

    // OpenAI API key, either provided directly or fetched from the user's environment variables
    string apiKey = string.Empty;

    // If API key is not provided directly, attempt to retrieve it from user environment variables
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
        apiKey = apiKeyInput;
    }
    string assistantIdInput = ""; // The assistant ID you want to use
    string assistantId = string.Empty;
    // If assistantId is not provided directly, attempt to retrieve it from user environment variables
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
        assistantId = assistantIdInput;
    }

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
    string userQuery =  
        $"list all tables form the DataModel.json file";
        //$"Modify existing comments or add new comments to the given measure while strictly preserving the original DAX code." +
        //$"Each comment should begin with '//' (for full-line comments) or '--' (for inline comments within a code line)." +
        //$"Analyze the DataModel.json file to gain insights into the data model, its business context, tables, table relationships, measures." +
        //$"Ensure the first few comment lines describe the measure's business purpose and context in simple terms." +
        //$"Add a few lines with a technical explanation of the measure, detailing the purpose of referenced tables, columns, and measures." +
        //$"Refer to the attached PDF files for a deeper understanding of the DAX language where necessary." +
        //$"For every important DAX statement, insert a comment line **before** the code explaining its function." +
        //$"Use inline comments ('--') only for additional clarification within a DAX line, avoiding excessive inline explanations." +
        //$"If the measure contains any DAX syntax errors, correct them and add a comment explicitly stating what was changed and why." +
        //$"Do not modify the core measure logic beyond necessary syntax corrections, and do not add extraneous text beyond required comments." +
        //$"Do not use any special characters or tags to define the code block. Ensure the updated measure is a valid DAX expression." +
        //$"Ensure the output strictly follows this JSON format: {{\"expression\":\"<put measure DAX expression here (and only here)>\",\"description\":\"<put measure description here>\",\"items\":\"<put a list of tables from DataModel>\",\"test\":\"<put a random joke here>\"}}." +
        //$"The output must not contain any additional characters outside the JSON structure." +
        //$"Add comments to short, single-line measures as well." +
        //$"in the end of the measure add a comment with a list of tables in the data model." +
        //$"Measure Expression: {measureExpression}";

    // Create the response format object using anonymous types
    var responseFormat = new
    {
        type = "json_schema",
        json_schema = new
        {
            name = "response_json_schema",
            description = "JSON format of the response",
            strict = true,
            schema = new
            {
                type = "object",
                properties = new
                {
                    expression = new
                    {
                        type = "string",
                        description = "put measure DAX expression here"
                    },
                    description = new
                    {
                        type = "string",
                        description = "put measure description here"
                    },
                    //items = new
                    //{
                    //    type = "string",
                    //    test = "put a list of tables in the data model here. Search DataModel.json for the table names. All tables. not just the ones used in this measure."
                    //},
                    //test = new
                    //{
                    //    type = "string",
                    //    test = "put a random joke here"
                    //},
                    //bomb = new
                    //{
                    //    type = "response",
                    //    bomb = ""
                    //}
                },
                //required = new string[] { "expression", "description", "items", "test", "bomb" },
                required = new string[] { "expression", "description" },
                additionalProperties = false
            }
        }
    };

    // Step 4: Create an HttpClient instance for making API requests
    using (var client = new System.Net.Http.HttpClient())
    {
        // Add authorization header with the API key
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        client.DefaultRequestHeaders.Add("OpenAI-Beta", "assistants=v2");

        // Step 5: Get the vector store associated with the assistant
        var vectorStoreId = GetVectorStoreForAssistant(client, baseUrl, assistantId);
        if (vectorStoreId == null)
        {
            Error("No vector store available to use.");
            return;
        }

        // Step 6: Create a new thread for the conversation with the vector store attached
        var threadPayload = new
        {
            tool_resources = new {
                file_search = new {
                    vector_store_ids = new[] { vectorStoreId }
                }
            }
        };

        var threadRequestBody = new System.Net.Http.StringContent(
            Newtonsoft.Json.JsonConvert.SerializeObject(threadPayload),
            System.Text.Encoding.UTF8,
            "application/json"
        );

        var threadResponse = client.PostAsync($"{baseUrl}/threads", threadRequestBody).Result;
        if (!threadResponse.IsSuccessStatusCode)
        {
            LogApiError(threadResponse);
            return; // Exit on API error
        }

        // Step 7: Parse the response to get the thread ID
        var threadContent = threadResponse.Content.ReadAsStringAsync().Result;
        var threadResult = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(threadContent);
        string threadId = threadResult.id.ToString(); // Extract thread ID

        // Step 8: Create a user message and add it to the thread
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

        // Step 9: Run the assistant using the thread and assistant ID
        var runPayload = new { 
            assistant_id = assistantId,
            model = model,
            temperature = temperature,
            //tools = new object[] {},
            tools = new object[] { new { type = "file_search" } },
            tool_choice = new { type = "file_search" },
            response_format = responseFormat     
        };

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

        // Step 10: Poll the run status until it's completed or failed
        int attempts = 1;
        while (runStatus != "completed" && runStatus != "failed" && attempts <= maxAttempts)
        {
            attempts++;
            System.Threading.Thread.Sleep(1000); // Wait 1 second before polling again
            var runStatusResponse = client.GetAsync($"{baseUrl}/threads/{threadId}/runs/{runId}").Result;

            if (!runStatusResponse.IsSuccessStatusCode)
            {
                LogApiError(runStatusResponse);
                return;
            }

            runResult = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(runStatusResponse.Content.ReadAsStringAsync().Result);
            runStatus = runResult.status.ToString();
        }

        // Step 11: Check if the assistant run failed or timed out
        if (attempts > maxAttempts)
        {
            Error("Operation timed out after reaching maximum attempts");
            Output(runResult);
            return;
        }
        if (runStatus != "completed")
        {
            Error($"Error: Run Status: {runStatus}");
            Output(runResult);
            return;
        }

        // Step 12: Retrieve the messages from the thread to get the assistant's response
        var messagesResponse = client.GetAsync($"{baseUrl}/threads/{threadId}/messages").Result;
        if (!messagesResponse.IsSuccessStatusCode)
        {
            LogApiError(messagesResponse);
            return;
        }

        var messagesResult = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(messagesResponse.Content.ReadAsStringAsync().Result);
        string updatedDaxExpression = string.Empty;

        // Step 13: Extract the AI-generated DAX expression from the assistant's message
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

        // Step 14: Create a backup of the original measure
        Guid newGuid = Guid.NewGuid(); // Generate a unique identifier
        string backupMeasureName = $"{selectedMeasure.Name}_backup_{newGuid}";

        var newMeasure = selectedMeasure.Table.AddMeasure(backupMeasureName, measureExpression);
        newMeasure.DisplayFolder = selectedMeasure.DisplayFolder;
        newMeasure.Description = selectedMeasure.Description;
        newMeasure.FormatString = selectedMeasure.FormatString;

        // Step 15: Update the selected measure with the AI-generated DAX code and description
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

// Function to get the vector store attached to the assistant
string GetVectorStoreForAssistant(System.Net.Http.HttpClient client, string baseUrl, string assistantId)
{
    // Retrieve the assistant's configuration
    var response = client.GetAsync($"{baseUrl}/assistants/{assistantId}").Result;

    if (!response.IsSuccessStatusCode)
    {
        var errorContent = response.Content.ReadAsStringAsync().Result;
        Error($"Failed to retrieve assistant configuration: {errorContent}");
        return null;
    }

    var assistantData = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(response.Content.ReadAsStringAsync().Result);

    // Check if vector_store_ids are present in the assistant's tool_resources
    if (assistantData.tool_resources?.file_search?.vector_store_ids != null)
    {
        return assistantData.tool_resources.file_search.vector_store_ids[0]?.ToString();
    }

    Error("No vector store found attached to this assistant.");
    return null;
}
