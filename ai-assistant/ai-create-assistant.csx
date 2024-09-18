// Note: Remove all 'using' directives

// Execute the function to create the DAX assistant
CreateDaxAssistant();

void CreateDaxAssistant()
{
    // Replace with your OpenAI API key
    string apiKey = "OPEN_AI_API_KEY";
    string baseUrl = "https://api.openai.com/v1";

    // Create HttpClient instance
    var client = new System.Net.Http.HttpClient();

    // Add the authorization header with the API key
    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    // Add the required OpenAI-Beta header
    client.DefaultRequestHeaders.Add("OpenAI-Beta", "assistants=v2");

    // Step 1: Export model data to JSON string
    string jsonContent = ExportModelToJsonString();

    // Convert the JSON string into a MemoryStream
    var memoryStream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(jsonContent));

    var formContent = new System.Net.Http.MultipartFormDataContent();
    var streamContent = new System.Net.Http.StreamContent(memoryStream);
    formContent.Add(streamContent, "file", "DataModel.json");
    formContent.Add(new System.Net.Http.StringContent("assistants"), "purpose");

    // Upload the file
    var uploadResponse = client.PostAsync($"{baseUrl}/files", formContent).Result;

    if (!uploadResponse.IsSuccessStatusCode)
    {
        var errorContent = uploadResponse.Content.ReadAsStringAsync().Result;
        Error($"File upload failed: {errorContent}");
        return;
    }

    var uploadResult = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(uploadResponse.Content.ReadAsStringAsync().Result);
    string fileId = uploadResult.id.ToString();
    Info($"File uploaded successfully. File ID: {fileId}");

    // Step 2: Create and configure the assistant
    var assistantPayload = new
    {
        name = "Tabular Editor DAX Assistant",
        instructions = "You are a DAX assistant for Tabular Editor. When asked, you comment, optimize, or suggest DAX code. You're aware of the data model (the attached DataModel.json files provides data model description).",
        model = "gpt-4o",

        // Adding the code interpreter tool
        tools = new[] { new { type = "code_interpreter" } },

        // Adding the tool resources section with the file ID attached to the code_interpreter tool
        tool_resources = new
        {
            code_interpreter = new
            {
                file_ids = new[] { fileId }
            }
        }
    };

    var assistantRequestBody = new System.Net.Http.StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(assistantPayload), System.Text.Encoding.UTF8, "application/json");
    var assistantResponse = client.PostAsync($"{baseUrl}/assistants", assistantRequestBody).Result;

    if (!assistantResponse.IsSuccessStatusCode)
    {
        var errorContent = assistantResponse.Content.ReadAsStringAsync().Result;
        Error($"Assistant creation failed: {errorContent}");
        return;
    }

    var assistantResult = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(assistantResponse.Content.ReadAsStringAsync().Result);
    string assistantId = assistantResult.id.ToString();

    // Copy assistant ID to the clipboard
    System.Windows.Forms.Clipboard.SetText(assistantId);

    Info($"Assistant created successfully. Assistant ID: {assistantId} (copied to clipboard)");
}

string ExportModelToJsonString()
{
    // Create the object that will hold the entire model structure
    var exportedModel = new Dictionary<string, object>
    {
        { "Tables", new List<Dictionary<string, object>>() },
        { "Relationships", new List<Dictionary<string, object>>() }
    };

    // Loop through all tables in the model
    foreach (var table in Model.Tables)
    {
        // Create a Dictionary to hold table metadata
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
            ((List<Dictionary<string, object>>)exportedTable["Measures"]).Add(exportedMeasure);
        }

        // Add the table to the exported model
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

        // Add the relationship to the exported model
        ((List<Dictionary<string, object>>)exportedModel["Relationships"]).Add(exportedRelationship);
    }

    // Serialize the exported model to JSON
    return Newtonsoft.Json.JsonConvert.SerializeObject(exportedModel, Newtonsoft.Json.Formatting.Indented);
}
