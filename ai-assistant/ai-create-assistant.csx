// This script creates a DAX assistant using the OpenAI API.
// It exports your Tabular model to JSON, uploads it, and configures an assistant.
// Finally, it copies the assistant ID to the clipboard for future use.

// See https://platform.openai.com/playground/assistants for the created assistants
// See https://platform.openai.com/storage for the uploaded files

// Execute the function to create the DAX assistant
CreateDaxAssistant();

void CreateDaxAssistant()
{
    // CONFIGURATION SECTION
    //To create an API key read https://help.openai.com/en/articles/9186755-managing-your-work-in-the-api-platform-with-projects
    string apiKey = "OPEN_AI_API_KEY";
    string baseUrl = "https://api.openai.com/v1";
    string model = "gpt-4o";
    string name = "Tabular Editor DAX Assistant";
    string instructions = "You are a DAX assistant for Tabular Editor. When asked, you comment, optimize, or suggest DAX code. You're aware of the data model (the attached DataModel.json file provides data model description).";
    // END OF CONFIGURATION

    // Create an HttpClient instance for making API requests
    var client = new System.Net.Http.HttpClient();

    // Add the authorization header with the API key
    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    // Add the required OpenAI-Beta header
    client.DefaultRequestHeaders.Add("OpenAI-Beta", "assistants=v2");

    // STEP 1: Export model data to JSON string and upload it to OpenAI storage
    string jsonContent = ExportModelToJsonString();
    
    // Save JSON file (optional)
    //SaveDataModelToFile(jsonContent);

    // Convert the JSON string into a MemoryStream for upload
    var memoryStream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(jsonContent));

    // Create multipart form data content for the file upload
    var formContent = new System.Net.Http.MultipartFormDataContent();
    var streamContent = new System.Net.Http.StreamContent(memoryStream);

    // Add the file content and purpose to the form data
    formContent.Add(streamContent, "file", "DataModel.json");
    formContent.Add(new System.Net.Http.StringContent("assistants"), "purpose");

    // Upload the file to OpenAI
    var uploadResponse = client.PostAsync($"{baseUrl}/files", formContent).Result;

    // Check if the file upload was successful
    if (!uploadResponse.IsSuccessStatusCode)
    {
        var errorContent = uploadResponse.Content.ReadAsStringAsync().Result;
        Error($"File upload failed: {errorContent}");
        return;
    }

    // Deserialize the response to get the file ID
    var uploadResult = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(uploadResponse.Content.ReadAsStringAsync().Result);
    string fileId = uploadResult.id.ToString();

    // STEP 2: Create and configure the assistant using the uploaded data model
    var assistantPayload = new
    {
        name = name,
        instructions = instructions,
        model = model,

        // Adding the code interpreter tool
        tools = new[] { new { type = "code_interpreter" } },

        // Attaching the data model file to the assistant's code interpreter tool
        tool_resources = new
        {
            code_interpreter = new
            {
                file_ids = new[] { fileId }
            }
        }
    };

    // Serialize the assistant configuration to JSON
    var assistantRequestBody = new System.Net.Http.StringContent(
        Newtonsoft.Json.JsonConvert.SerializeObject(assistantPayload),
        System.Text.Encoding.UTF8,
        "application/json"
    );

    // Send a request to create the assistant
    var assistantResponse = client.PostAsync($"{baseUrl}/assistants", assistantRequestBody).Result;

    // Check if the assistant creation was successful
    if (!assistantResponse.IsSuccessStatusCode)
    {
        var errorContent = assistantResponse.Content.ReadAsStringAsync().Result;
        Error($"Assistant creation failed: {errorContent}");
        return;
    }

    // Deserialize the response to get the assistant ID
    var assistantResult = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(assistantResponse.Content.ReadAsStringAsync().Result);
    string assistantId = assistantResult.id.ToString();

    // Copy the assistant ID to the clipboard for future use
    System.Windows.Forms.Clipboard.SetText(assistantId);

    // Inform the user that the assistant was created successfully
    Info($"Assistant created successfully. Assistant ID: {assistantId} (copied to clipboard)");
}

string ExportModelToJsonString()
{
    // Create a dictionary to hold the entire model structure
    var exportedModel = new Dictionary<string, object>
    {
        { "Tables", new List<Dictionary<string, object>>() },
        { "Relationships", new List<Dictionary<string, object>>() }
    };

    // Loop through all tables in the model
    foreach (var table in Model.Tables)
    {
        // Create a dictionary to hold table metadata
        var exportedTable = new Dictionary<string, object>
        {
            { "TableName", table.Name },
            { "Columns", new List<Dictionary<string, object>>() },
            { "Measures", new List<Dictionary<string, object>>() }
        };

        // Export columns
        foreach (var column in table.Columns)
        {
            var exportedColumn = new Dictionary<string, object>
            {
                { "ColumnName", column.Name },
                { "DataType", column.DataType.ToString() },
                { "Expression", column is CalculatedColumn ? ((CalculatedColumn)column).Expression : null }
            };
            // Add the column to the table's columns list
            ((List<Dictionary<string, object>>)exportedTable["Columns"]).Add(exportedColumn);
        }

        // Export measures
        foreach (var measure in table.Measures)
        {
            var exportedMeasure = new Dictionary<string, object>
            {
                { "MeasureName", measure.Name },
                { "Expression", measure.Expression }
            };
            // Add the measure to the table's measures list
            ((List<Dictionary<string, object>>)exportedTable["Measures"]).Add(exportedMeasure);
        }

        // Add the table to the model's tables list
        ((List<Dictionary<string, object>>)exportedModel["Tables"]).Add(exportedTable);
    }

    // Export relationships
    foreach (var relationship in Model.Relationships)
    {
        var exportedRelationship = new Dictionary<string, object>
        {
            { "FromTable", relationship.FromTable.Name },
            { "FromColumn", relationship.FromColumn.Name },
            { "ToTable", relationship.ToTable.Name },
            { "ToColumn", relationship.ToColumn.Name },
            { "IsActive", relationship.IsActive }
        };
        // Add the relationship to the model's relationships list
        ((List<Dictionary<string, object>>)exportedModel["Relationships"]).Add(exportedRelationship);
    }

    // Serialize the exported model to JSON with indentation for readability
    return Newtonsoft.Json.JsonConvert.SerializeObject(exportedModel, Newtonsoft.Json.Formatting.Indented);
}

// OPTIONAL FUNCTION: Save the DataModel.json to a file (commented out by default)
void SaveDataModelToFile(string jsonContent)
{
    // Specify the file path where you want to save the DataModel.json
    string filePath = @"D:\DataModel.json"; // Update the path as needed

    try
    {
        // Write the JSON content to the specified file
        System.IO.File.WriteAllText(filePath, jsonContent);

        // Inform the user that the file was saved successfully
        Info($"DataModel.json has been saved to {filePath}");
    }
    catch (System.Exception ex)
    {
        // Handle any exceptions that occur during file writing
        Error($"Failed to save DataModel.json: {ex.Message}");
    }
}
