using Newtonsoft.Json.Linq;

// Execute the function to create the DAX assistant
CreateDaxAssistant();

void CreateDaxAssistant()
{
    // CONFIGURATION SECTION
    string apiKey = "OPEN_AI_API_KEY"; // Replace with your OpenAI API key
    string baseUrl = "https://api.openai.com/v1";
    string model = "gpt-4o";
    string name = "Tabular Editor DAX Assistant";
    string instructions = "You are a DAX assistant for Tabular Editor. Your tasks include commenting, optimizing, and suggesting DAX code. Utilize the attached DataModel.json file to understand the data model context. Utilize the attached The Definitive Guide to DAX.pdf file for better understanding of DAX language.";
    string pdfFileName = "The Definitive Guide to DAX.pdf"; // Name of your PDF file
    string pdfFilePath = @"D:\BI LIBRARY\+ The Definitive Guide to DAX - Marco Russo\The Definitive Guide to DAX.pdf"; // Path to your PDF file
    // END OF CONFIGURATION

    // Create an HttpClient instance for making API requests
    var client = new System.Net.Http.HttpClient();

    // Add the authorization header with the API key
    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    client.DefaultRequestHeaders.Add("OpenAI-Beta", "assistants=v2");

    // STEP 1: Generate DataModel.json content from memory
    string jsonContent = ExportModelToJsonString();

    // Save JSON file (optional)
    //SaveDataModelToFile(jsonContent);

    // STEP 2: Upload DataModel.json from memory
    string dataModelFileId = UploadFileFromMemory(client, baseUrl, jsonContent, "DataModel.json");
    if (string.IsNullOrEmpty(dataModelFileId)) return;

    // STEP 3: Upload the PDF file from local storage
    string pdfFileId = UploadFile(client, baseUrl, pdfFilePath, pdfFileName);
    if (string.IsNullOrEmpty(pdfFileId)) return;

    // STEP 4: Create a vector store from the uploaded files
    string vectorStoreId = CreateVectorStore(client, baseUrl, new[] { dataModelFileId, pdfFileId });
    if (string.IsNullOrEmpty(vectorStoreId)) return;

    // STEP 5: Create and configure the assistant using the uploaded files
    var assistantPayload = new
    {
        name = name,
        instructions = instructions,
        model = model,
        tools = new[] { new { type = "file_search" } },
        tool_resources = new
        {
            file_search = new
            {
                vector_store_ids = new[] { vectorStoreId }
            }
        }
    };

    var assistantRequestBody = new System.Net.Http.StringContent(
        Newtonsoft.Json.JsonConvert.SerializeObject(assistantPayload),
        System.Text.Encoding.UTF8,
        "application/json"
    );

    var assistantResponse = client.PostAsync($"{baseUrl}/assistants", assistantRequestBody).Result;

    if (!assistantResponse.IsSuccessStatusCode)
    {
        var errorContent = assistantResponse.Content.ReadAsStringAsync().Result;
        Error($"Assistant creation failed: {errorContent}");
        return;
    }

    var assistantResult = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(assistantResponse.Content.ReadAsStringAsync().Result);
    string assistantId = assistantResult.id.ToString();

    // Copy the assistant ID to the clipboard for future use
    System.Windows.Forms.Clipboard.SetText(assistantId);

    Info($"Assistant created successfully. Assistant ID: {assistantId} (copied to clipboard)");
}

// Function to upload a JSON file from memory instead of from disk
string UploadFileFromMemory(System.Net.Http.HttpClient client, string baseUrl, string jsonContent, string fileName)
{
    try
    {
        var memoryStream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(jsonContent));
        var formContent = new System.Net.Http.MultipartFormDataContent();
        var streamContent = new System.Net.Http.StreamContent(memoryStream);
        formContent.Add(streamContent, "file", fileName);
        formContent.Add(new System.Net.Http.StringContent("assistants"), "purpose");

        var uploadResponse = client.PostAsync($"{baseUrl}/files", formContent).Result;

        if (!uploadResponse.IsSuccessStatusCode)
        {
            var errorContent = uploadResponse.Content.ReadAsStringAsync().Result;
            Error($"File upload failed: {errorContent}");
            return null;
        }

        var uploadResult = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(uploadResponse.Content.ReadAsStringAsync().Result);
        return uploadResult.id.ToString();
    }
    catch (Exception ex)
    {
        Error($"File upload error: {ex.Message}");
        return null;
    }
}

string UploadFile(System.Net.Http.HttpClient client, string baseUrl, string filePath, string fileName)
{
    try
    {
        var memoryStream = new System.IO.MemoryStream(System.IO.File.ReadAllBytes(filePath));
        var formContent = new System.Net.Http.MultipartFormDataContent();
        var streamContent = new System.Net.Http.StreamContent(memoryStream);
        formContent.Add(streamContent, "file", fileName);
        formContent.Add(new System.Net.Http.StringContent("assistants"), "purpose");

        var uploadResponse = client.PostAsync($"{baseUrl}/files", formContent).Result;

        if (!uploadResponse.IsSuccessStatusCode)
        {
            var errorContent = uploadResponse.Content.ReadAsStringAsync().Result;
            Error($"File upload failed: {errorContent}");
            return null;
        }

        var uploadResult = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(uploadResponse.Content.ReadAsStringAsync().Result);
        return uploadResult.id.ToString();
    }
    catch (Exception ex)
    {
        Error($"File upload error: {ex.Message}");
        return null;
    }
}

string CreateVectorStore(System.Net.Http.HttpClient client, string baseUrl, string[] fileIds)
{
    var vectorStorePayload = new
    {
        file_ids = fileIds
    };

    var vectorStoreRequestBody = new System.Net.Http.StringContent(
        Newtonsoft.Json.JsonConvert.SerializeObject(vectorStorePayload),
        System.Text.Encoding.UTF8,
        "application/json"
    );

    var vectorStoreResponse = client.PostAsync($"{baseUrl}/vector_stores", vectorStoreRequestBody).Result;

    if (!vectorStoreResponse.IsSuccessStatusCode)
    {
        var errorContent = vectorStoreResponse.Content.ReadAsStringAsync().Result;
        Error($"Vector store creation failed: {errorContent}");
        return null;
    }

    var vectorStoreResult = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(vectorStoreResponse.Content.ReadAsStringAsync().Result);
    return vectorStoreResult.id.ToString();
}

// Function to export model to JSON string (from memory)
string ExportModelToJsonString()
{
    var modelJson = new JObject();

    // Loop through each table in the model
    var tables = new JArray();
    foreach (var table in Model.Tables)
    {
        // Check if the table is a calculation group
        if (table is CalculationGroupTable calcGroupTable)
        {
            var calcGroupJson = GetCalculationGroupMetadata(calcGroupTable);
            tables.Add(calcGroupJson);
        }
        else
        {
            // Handle regular tables
            tables.Add(GetTableMetadata(table));
        }
    }
    modelJson["Tables"] = tables;

    // Relationships
    var relationships = new JArray();
    foreach (var relationship in Model.Relationships)
    {
        var relJson = new JObject
        {
            { "FromTable", relationship.FromTable.Name },
            { "FromColumn", relationship.FromColumn.Name },
            { "ToTable", relationship.ToTable.Name },
            { "ToColumn", relationship.ToColumn.Name },
            { "IsActive", relationship.IsActive }
        };
        relationships.Add(relJson);
    }
    modelJson["Relationships"] = relationships;

    // Serialize the exported model to JSON with indentation for readability
    return Newtonsoft.Json.JsonConvert.SerializeObject(modelJson, Newtonsoft.Json.Formatting.Indented);
}

JObject GetTableMetadata(Table table)
{
    var tableJson = new JObject
    {
        ["Name"] = table.Name,
        ["Description"] = table.Description,
        ["IsCalculatedTable"] = table.Partitions.Any(p => !string.IsNullOrEmpty(p.Expression))
    };

    // Columns
    var columns = new JArray();
    foreach (var column in table.Columns)
    {
        var columnJson = new JObject
        {
            ["Name"] = column.Name,
            ["DataType"] = column.DataType.ToString(),
            ["IsHidden"] = column.IsHidden,
            ["FormatString"] = column.FormatString
        };

        if (column is CalculatedColumn calculatedColumn)
        {
            columnJson["IsCalculatedColumn"] = true;
            columnJson["Expression"] = calculatedColumn.Expression;
        }
        else
        {
            columnJson["IsCalculatedColumn"] = false;
        }
        columns.Add(columnJson);
    }
    tableJson["Columns"] = columns;

    // Measures
    var measures = new JArray();
    foreach (var measure in table.Measures)
    {
        var measureJson = new JObject
        {
            ["Name"] = measure.Name,
            ["Expression"] = measure.Expression,
            ["FormatString"] = measure.FormatString,
            ["IsHidden"] = measure.IsHidden
        };
        measures.Add(measureJson);
    }
    tableJson["Measures"] = measures;

    return tableJson;
}

JObject GetCalculationGroupMetadata(CalculationGroupTable calcGroupTable)
{
    var calcGroupJson = new JObject
    {
        ["Name"] = calcGroupTable.Name,
        ["Description"] = calcGroupTable.Description
    };

    // Calculation Items
    var calcItems = new JArray();
    foreach (var item in calcGroupTable.CalculationItems)
    {
        var itemJson = new JObject
        {
            ["Name"] = item.Name,
            ["Expression"] = item.Expression
        };
        calcItems.Add(itemJson);
    }
    calcGroupJson["CalculationItems"] = calcItems;

    // Measures within Calculation Groups
    var calcGroupMeasures = new JArray();
    foreach (var measure in calcGroupTable.Measures)
    {
        var measureJson = new JObject
        {
            ["Name"] = measure.Name,
            ["Expression"] = measure.Expression
        };
        calcGroupMeasures.Add(measureJson);
    }
    calcGroupJson["Measures"] = calcGroupMeasures;

    // Calculated Columns within Calculation Groups
    var calcColumns = new JArray();
    foreach (var column in calcGroupTable.Columns)
    {
        if (column is CalculatedColumn calculatedColumn)
        {
            var columnJson = new JObject
            {
                ["Name"] = calculatedColumn.Name,
                ["Expression"] = calculatedColumn.Expression,
                ["DataType"] = calculatedColumn.DataType.ToString(),
                ["IsHidden"] = calculatedColumn.IsHidden,
                ["FormatString"] = calculatedColumn.FormatString
            };
            calcColumns.Add(columnJson);
        }
    }
    calcGroupJson["CalculatedColumns"] = calcColumns;

    return calcGroupJson;
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
