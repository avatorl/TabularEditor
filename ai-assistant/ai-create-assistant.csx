using Newtonsoft.Json.Linq;
using System;
using System.Windows.Forms;
using Microsoft.Win32;

// CONFIGURATION =======================================
// API base URL
string baseUrl = "https://api.openai.com/v1";
// OpenAI model
string model = "gpt-4o";
// File path to export tabular metadata as JSON file
string filePath = @"D:\DataModel.json";
// Define instructions for the assistant
string instructions = "You are an assistant specialized in DAX and M languages for Tabular Editor. Your task is to comment on DAX and M code. " +
                      "Use the attached DataModel.json to understand the data model context. Refer to the other attached files for additional details on DAX and M languages.";
// Define array of file names (for OpenAI storage)
string[] pdfFileNames = {
    "The Definitive Guide to DAX.pdf",
    "Power Query M Formula Language Specification (July 2019).pdf"
};
// Define array of local file paths
string[] pdfFilePaths = {
    @"D:\BI LIBRARY\+ The Definitive Guide to DAX - Marco Russo\The Definitive Guide to DAX.pdf",
    @"D:\BI LIBRARY\Power Query M Formula Language Specification (July 2019).pdf"
};
// ======================================================

// Execute the function to create or update the DAX assistant
CreateOrUpdateDaxAssistant();

/// Creates or updates the DAX assistant in OpenAI API.
void CreateOrUpdateDaxAssistant()
{

    // Initialize HttpClient for making API requests
    var client = new System.Net.Http.HttpClient();

    // OpenAI API key, either provided directly or fetched from the user's environment variables
    string apiKeyInput = ""; // Your OpenAI API key, or leave blank to use environment variable
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

    string assistantId = string.Empty;
    // If assistantId is not provided directly, attempt to retrieve it from user environment variables
    {
        using (RegistryKey userKey = Registry.CurrentUser.OpenSubKey(@"Environment"))
        {
            if (userKey != null)
            {
                assistantId = userKey.GetValue("OPENAI_TE_ASSISTANT_ID") as string;
            }
        }
    }

    //Output(assistantId);

    // Array to store the file IDs in storage
    string[] pdfFileIdsInTheStorage = new string[pdfFileNames.Length];

    // Loop through the array and handle each book
    for (int i = 0; i < pdfFileNames.Length; i++)
    {
        string pdfFileName = pdfFileNames[i];

        // Get the file ID for each book and store it in the pdfFileIdsInTheStorage array
        pdfFileIdsInTheStorage[i] = GetFileIdByName(client, baseUrl, apiKey, pdfFileName);
    }

    // Add authorization header with API key using AuthenticationHeaderValue
    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
    client.DefaultRequestHeaders.Add("OpenAI-Beta", "assistants=v2");

    // If existing assistant found then update it
    if (CheckAssistantExistsById(client,baseUrl,assistantId)==true)  {
        UpdateAssistant(client, assistantId, baseUrl, apiKey, model, instructions, pdfFileNames, pdfFilePaths, pdfFileIdsInTheStorage);
    }
    // If no existing assistant is found, create a new assistant
    else
    {
        CreateDaxAssistant(client, baseUrl, model, $"Assistant for Tabular Editor", instructions, pdfFileNames, pdfFilePaths, pdfFileIdsInTheStorage);
    }
}

// Function to check if an assistant with a certain ID exists in the OpenAI API
bool CheckAssistantExistsById(System.Net.Http.HttpClient client, string baseUrl, string assistantId)
{
    try
    {
        // Make an HTTP GET request to fetch the assistant by ID
        var response = client.GetAsync($"{baseUrl}/assistants/{assistantId}").Result;

        // Check if the response was unsuccessful
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = response.Content.ReadAsStringAsync().Result;
            Error($"Failed to retrieve assistant with ID {assistantId}: {errorContent}");
            return false; // Return false on failure
        }

        // Parse the response content as a string
        var responseContent = response.Content.ReadAsStringAsync().Result;

        // Check if the response content is empty
        if (string.IsNullOrEmpty(responseContent))
        {
            Error("Received empty response from the API");
            return false; // Return false if the response is empty
        }

        // Parse the response content into a JObject
        var parsedResponse = Newtonsoft.Json.Linq.JObject.Parse(responseContent);

        // Ensure that the parsed response contains an "assistant" field
        if (parsedResponse == null || parsedResponse["id"] == null)
        {
            Error("Parsed response or 'id' field is null");
            return false; // Return false if parsing fails
        }

        // Extract the assistant's ID from the response
        string retrievedId = parsedResponse["id"]?.ToString();

        // Return true if the retrieved ID matches the input ID
        return !string.IsNullOrEmpty(retrievedId) && retrievedId == assistantId;
    }
    catch (Exception ex)
    {
        // Log the exception and return false on error
        Error($"An exception occurred while checking assistant by ID: {ex.Message}");
        return false;
    }
}

// Function to retrieve the list of files associated with a vector store
List<string> GetFilesForVectorStore(System.Net.Http.HttpClient client, string baseUrl, string vectorStoreId)
{
    // Send request to get the files in the vector store
    var response = client.GetAsync($"{baseUrl}/vector_stores/{vectorStoreId}/files").Result;

    // Handle unsuccessful response
    if (!response.IsSuccessStatusCode)
    {
        var errorContent = response.Content.ReadAsStringAsync().Result;
        Error($"Failed to retrieve files for vector store {vectorStoreId}: {errorContent}");
        return null;
    }

    // Deserialize the response to extract file IDs
    var filesData = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(response.Content.ReadAsStringAsync().Result);
    List<string> fileIds = new List<string>();

    // Loop through the files and add their IDs to the list
    foreach (var file in filesData["data"])
    {
        fileIds.Add(file["id"].ToString());
    }

    return fileIds; // Return the list of file IDs
}

// Function to delete a file from storage
bool DeleteFile(System.Net.Http.HttpClient client, string baseUrl, string fileId)
{
    // Send request to delete the file
    var deleteResponse = client.DeleteAsync($"{baseUrl}/files/{fileId}").Result;

    // Handle unsuccessful deletion
    if (!deleteResponse.IsSuccessStatusCode)
    {
        var errorContent = deleteResponse.Content.ReadAsStringAsync().Result;
        Error($"Failed to delete file with ID: {fileId}, Error: {errorContent}");
        return false;
    }

    // File deleted successfully
    return true;
}

// Function to retrieve the vector store ID associated with an assistant
string GetExistingVectorStoreId(System.Net.Http.HttpClient client, string baseUrl, string assistantId)
{
    // Ensure there is no trailing slash in the base URL to prevent double slashes in the request URI
    if (baseUrl.EndsWith("/"))
    {
        baseUrl = baseUrl.TrimEnd('/');
    }

    // Send request to retrieve the assistant's data
    var response = client.GetAsync($"{baseUrl}/assistants/{assistantId}").Result;

    // Handle unsuccessful response
    if (!response.IsSuccessStatusCode)
    {
        var errorContent = response.Content.ReadAsStringAsync().Result;
        Error($"Failed to retrieve assistant data: {errorContent}");
        return null;
    }

    // Deserialize the response to extract the vector store ID
    var assistantData = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(response.Content.ReadAsStringAsync().Result);

    // Check if there are any vector store IDs associated with the assistant
    if (assistantData.tool_resources?.file_search?.vector_store_ids != null && assistantData.tool_resources.file_search.vector_store_ids.Count > 0)
    {
        // Return the first vector store ID
        return assistantData.tool_resources.file_search.vector_store_ids[0]?.ToString();
    }
    else
    {
        Error("Vector store information not found.");
        return null;
    }
}

// Function to create a new DAX assistant
void CreateDaxAssistant(System.Net.Http.HttpClient client, string baseUrl, string model, string name, string instructions, string[] pdfFileNames, string[] pdfFilePaths, string[] pdfFileIds)
{
    // STEP 1: Generate DataModel.json content
    string jsonContent = ExportModelToJsonString();

    // Optional: save DataModel.json
    SaveDataModelToFile(jsonContent);

    // STEP 2: Upload DataModel.json to the API
    string dataModelFileId = UploadFileFromMemory(client, baseUrl, jsonContent, "DataModel.json");
    if (string.IsNullOrEmpty(dataModelFileId)) return;

// STEP 3: Upload multiple PDF files
for (int i = 0; i < pdfFileNames.Length; i++)
{
        string pdfFileName = pdfFileNames[i];
        string pdfFilePath = pdfFilePaths[i];

        pdfFileIds[i] = UploadFile(client, baseUrl, pdfFilePath, pdfFileName);
        if (string.IsNullOrEmpty(pdfFileIds[i]))
        {
            Error("Failed to upload new PDF file.");
            return; // Exit if the PDF upload fails
        }
    
}

    // STEP 4: Create a vector store from the uploaded files
    // Combine dataModelFileId and pdfFileIds into a single array
    string[] allFileIds = new[] { dataModelFileId }.Concat(pdfFileIds).ToArray();
    string vectorStoreId = CreateVectorStore(client, baseUrl, allFileIds);
    if (string.IsNullOrEmpty(vectorStoreId)) return;

    // STEP 5: Create and configure the assistant with the vector store
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

    // Send the request to create the assistant
    var assistantResponse = client.PostAsync($"{baseUrl}/assistants", assistantRequestBody).Result;

    // Handle unsuccessful assistant creation
    if (!assistantResponse.IsSuccessStatusCode)
    {
        var errorContent = assistantResponse.Content.ReadAsStringAsync().Result;
        Error($"Assistant creation failed: {errorContent}");
        return;
    }

    // Extract the assistant ID from the response and copy it to the clipboard
    var assistantResult = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(assistantResponse.Content.ReadAsStringAsync().Result);
    string assistantId = assistantResult.id.ToString();
    System.Windows.Forms.Clipboard.SetText(assistantId);

     // Set the environment variable for the current user (put assistant id into the variable)
    Environment.SetEnvironmentVariable("OPENAI_TE_ASSISTANT_ID", assistantId, EnvironmentVariableTarget.User);

    Info($"Assistant created successfully. Assistant ID: {assistantId} (copied to clipboard)");
}

// Function to update an existing assistant by replacing the vector store with new data
void UpdateAssistant(System.Net.Http.HttpClient client, string assistantId, string baseUrl, string apiKey, string model, string instructions, string[] pdfFileNames, string[] pdfFilePaths, string[] pdfFileIds)
{
    // STEP 1: Retrieve and delete the existing vector store associated with the assistant
    string vectorStoreId = GetExistingVectorStoreId(client, baseUrl, assistantId);
    
    // If there is an existing vector store, delete it along with associated files
    if (!string.IsNullOrEmpty(vectorStoreId))
    {
        bool storeDeleted = DeleteVectorStoreAndFiles(client, baseUrl, apiKey, vectorStoreId, pdfFileIds);
        if (!storeDeleted)
        {
            Error("Failed to delete the existing vector store.");
            return; // Exit if the store deletion fails
        }
    }

    // STEP 2: Upload the new DataModel.json file generated from memory
    string jsonContent = ExportModelToJsonString(); // Generate JSON content from the model

    // Optional: save DataModel.json
    SaveDataModelToFile(jsonContent);

    string dataModelFileId = UploadFileFromMemory(client, baseUrl, jsonContent, "DataModel.json");
    
    // Ensure the DataModel.json file is uploaded successfully
    if (string.IsNullOrEmpty(dataModelFileId))
    {
        Error("Failed to upload new DataModel.json file.");
        return; // Exit if the file upload fails
    }

// STEP 3: Upload multiple PDF files
for (int i = 0; i < pdfFileNames.Length; i++)
{
        string pdfFileName = pdfFileNames[i];
        string pdfFilePath = pdfFilePaths[i];

        if (pdfFileIds[i]=="") {
        pdfFileIds[i] = UploadFile(client, baseUrl, pdfFilePath, pdfFileName);
        if (string.IsNullOrEmpty(pdfFileIds[i]))
        {
            Error("Failed to upload new PDF file.");
            return; // Exit if the PDF upload fails
        }
        }
    
}

    // Combine dataModelFileId and pdfFileIds into a single array
    string[] allFileIds = new[] { dataModelFileId }.Concat(pdfFileIds).ToArray();

    // STEP 4: Create a new vector store with the uploaded files (DataModel.json and PDF)
    string newVectorStoreId = CreateVectorStore(client, baseUrl, allFileIds);
    
    // Ensure the vector store creation was successful
    if (string.IsNullOrEmpty(newVectorStoreId)) return; // Exit if the vector store creation fails

    // STEP 5: Prepare the payload to update the assistant with the new vector store
    var updatePayload = new
    {
        instructions = instructions, // New instructions for the assistant
        model = model,               // AI model to use (e.g., gpt-4o)
        tools = new[] { new { type = "file_search" } }, // Assistant tools (file search)
        tool_resources = new
        {
            file_search = new
            {
                vector_store_ids = new[] { newVectorStoreId } // Link the new vector store
            }
        }
    };

    // Convert the payload to JSON and send a POST request to update the assistant
    var updateRequestBody = new System.Net.Http.StringContent(
        Newtonsoft.Json.JsonConvert.SerializeObject(updatePayload),
        System.Text.Encoding.UTF8,
        "application/json"
    );

    var updateResponse = client.PostAsync($"{baseUrl}/assistants/{assistantId}", updateRequestBody).Result;

    // Check if the update was successful and log the result
    if (!updateResponse.IsSuccessStatusCode)
    {
        var errorContent = updateResponse.Content.ReadAsStringAsync().Result;
        Error($"Assistant update failed: {errorContent}");
    }
    else
    {
        Info($"Assistant '{assistantId}' updated successfully with the new DataModel.json.");
    }
}

// Function to delete the existing vector store and its associated files
bool DeleteVectorStoreAndFiles(System.Net.Http.HttpClient client, string baseUrl, string apiKey, string vectorStoreId, string[] pdfFileIds)
{
    // Step 1: Check if a vector store ID is provided
    if (string.IsNullOrEmpty(vectorStoreId))
    {
        // No vector store to delete, so return true
        return true;
    }

    // Step 2: Retrieve the list of files associated with the vector store
    var fileIds = GetFilesForVectorStore(client, baseUrl, vectorStoreId);

    // Step 3: Check if there are files to delete
    if (fileIds != null && fileIds.Count > 0)
    {
        // Step 4: Loop through the files and delete each one, except the PDF file
        foreach (var fileId in fileIds)
        {
            if (fileId==GetFileIdByName(client, baseUrl, apiKey, "DataModel.json")) // Skip the PDF file
            {
                bool fileDeleted = DeleteFile(client, baseUrl, fileId);
                if (!fileDeleted)
                {
                    Error($"Failed to delete file with ID: {fileId}. Aborting further deletions.");
                    return false; // Stop further deletions if any fail
                }
            }
        }
    }

    // Step 5: Delete the vector store itself
    var deleteResponse = client.DeleteAsync($"{baseUrl}/vector_stores/{vectorStoreId}").Result;
    if (!deleteResponse.IsSuccessStatusCode)
    {
        var errorContent = deleteResponse.Content.ReadAsStringAsync().Result;
        Error($"Failed to delete vector store with ID: {vectorStoreId}, Error: {errorContent}");
        return false;
    }

    // Vector store and associated files deleted successfully
    return true;
}

// Function to upload a JSON file from memory to the OpenAI API storage
string UploadFileFromMemory(System.Net.Http.HttpClient client, string baseUrl, string jsonContent, string fileName)
{
    try
    {
        // Create a memory stream from the JSON content
        var memoryStream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(jsonContent));

        // Prepare the multipart form data for the file upload
        var formContent = new System.Net.Http.MultipartFormDataContent();
        var streamContent = new System.Net.Http.StreamContent(memoryStream);
        formContent.Add(streamContent, "file", fileName);
        formContent.Add(new System.Net.Http.StringContent("assistants"), "purpose"); // Define the file purpose

        // Execute the POST request to upload the file
        var uploadResponse = client.PostAsync($"{baseUrl}/files", formContent).Result;

        // Check for successful response
        if (!uploadResponse.IsSuccessStatusCode)
        {
            var errorContent = uploadResponse.Content.ReadAsStringAsync().Result;
            Error($"File upload failed: {errorContent}");
            return null;
        }

        // Parse the response and return the file ID
        var uploadResult = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(uploadResponse.Content.ReadAsStringAsync().Result);
        return uploadResult.id.ToString();
    }
    catch (Exception ex)
    {
        Error($"File upload error: {ex.Message}");
        return null;
    }
}

// Function to upload a file from the file system to the OpenAI API storage
string UploadFile(System.Net.Http.HttpClient client, string baseUrl, string filePath, string fileName)
{
    try
    {
        // Read the file into a memory stream
        var memoryStream = new System.IO.MemoryStream(System.IO.File.ReadAllBytes(filePath));

        // Prepare the multipart form data for the file upload
        var formContent = new System.Net.Http.MultipartFormDataContent();
        var streamContent = new System.Net.Http.StreamContent(memoryStream);
        formContent.Add(streamContent, "file", fileName);
        formContent.Add(new System.Net.Http.StringContent("assistants"), "purpose"); // Define the file purpose

        // Execute the POST request to upload the file
        var uploadResponse = client.PostAsync($"{baseUrl}/files", formContent).Result;

        // Check for successful response
        if (!uploadResponse.IsSuccessStatusCode)
        {
            var errorContent = uploadResponse.Content.ReadAsStringAsync().Result;
            Error($"File upload failed: {errorContent}");
            return null;
        }

        // Parse the response and return the file ID
        var uploadResult = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(uploadResponse.Content.ReadAsStringAsync().Result);
        return uploadResult.id.ToString();
    }
    catch (Exception ex)
    {
        Error($"File upload error: {ex.Message}");
        return null;
    }
}

// Function to create a vector store in the OpenAI API, which contains file references
string CreateVectorStore(System.Net.Http.HttpClient client, string baseUrl, string[] fileIds)
{
    // Create the payload with file IDs to be included in the vector store
    var vectorStorePayload = new
    {
        file_ids = fileIds,
        name = $"TE Assistant Vector Store" // Name the vector store
    };

    // Convert the payload to JSON and send a POST request to create the vector store
    var vectorStoreRequestBody = new System.Net.Http.StringContent(
        Newtonsoft.Json.JsonConvert.SerializeObject(vectorStorePayload),
        System.Text.Encoding.UTF8,
        "application/json"
    );

    // Execute the request to create the vector store
    var vectorStoreResponse = client.PostAsync($"{baseUrl}/vector_stores", vectorStoreRequestBody).Result;

    // Check for successful response
    if (!vectorStoreResponse.IsSuccessStatusCode)
    {
        var errorContent = vectorStoreResponse.Content.ReadAsStringAsync().Result;
        Error($"Vector store creation failed: {errorContent}");
        return null;
    }

    // Parse and return the vector store ID
    var vectorStoreResult = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(vectorStoreResponse.Content.ReadAsStringAsync().Result);
    return vectorStoreResult.id.ToString();
}

// Function to export the current data model to a JSON string from memory
string ExportModelToJsonString()
{
    var modelJson = new JObject();

    // Add tables and calculation groups to the JSON structure
    var tables = new JArray();
    foreach (var table in Model.Tables)
    {
        // Handle calculation groups differently from regular tables
        if (table is CalculationGroupTable calcGroupTable)
        {
            var calcGroupJson = GetCalculationGroupMetadata(calcGroupTable);
            tables.Add(calcGroupJson);
        }
        else
        {
            var tableJson = GetTableMetadata(table);
            tables.Add(tableJson);
        }
    }
    modelJson["tables"] = tables;

    // Add relationships to the JSON structure
    var relationships = new JArray();
    foreach (var relationship in Model.Relationships)
    {
        var relJson = new JObject
        {
            { "name", relationship.Name },
            { "isActive", relationship.IsActive },
            { "crossFilteringBehavior", relationship.CrossFilteringBehavior.ToString() },
            { "securityFilteringBehavior", relationship.SecurityFilteringBehavior.ToString() },
            { "fromCardinality", relationship.FromCardinality.ToString() },
            { "toCardinality", relationship.ToCardinality.ToString() },
            { "fromColumn", relationship.FromColumn?.Name ?? "None" }, // Null check
            { "fromTable", relationship.FromTable?.Name ?? "None" },   // Null check
            { "toColumn", relationship.ToColumn?.Name ?? "None" },     // Null check
            { "toTable", relationship.ToTable?.Name ?? "None" }        // Null check
        };
        relationships.Add(relJson);
    }
    modelJson["relationships"] = relationships;

    // Serialize the entire model to JSON with indentation for readability
    return Newtonsoft.Json.JsonConvert.SerializeObject(modelJson, Newtonsoft.Json.Formatting.Indented);
}

// Helper function to get metadata for regular tables
JObject GetTableMetadata(Table table)
{
    // Check if the table is a calculated table (DAX) by checking the partition's DAX expression
    bool isCalculatedTable = table.Partitions.Any(p => !string.IsNullOrEmpty(p.Expression) && p.SourceType == PartitionSourceType.Calculated);

    // Add basic table metadata
    var tableJson = new JObject
    {
        ["name"] = table.Name,
        ["description"] = table.Description ?? string.Empty,  // Null check
        ["isCalculatedTable"] = isCalculatedTable // Only true for DAX-calculated tables
    };

    // Add the expression for both DAX-calculated tables and M query tables
    var partition = table.Partitions.FirstOrDefault();
    if (partition != null)
    {
        tableJson["expression"] = partition.Expression ?? string.Empty; // Null check for partition expression
    }

    // Add columns to the table metadata
    var columns = new JArray();
    foreach (var column in table.Columns)
    {
        var columnJson = new JObject
        {
            ["name"] = column.Name,
            ["dataType"] = column.DataType.ToString(),
            ["dataCategory"] = column.DataCategory?.ToString() ?? "Uncategorized", // Null check
            ["description"] = column.Description ?? string.Empty,  // Null check
            ["isHidden"] = column.IsHidden,
            ["formatString"] = column.FormatString ?? string.Empty, // Null check
            ["displayFolder"] = column.DisplayFolder ?? string.Empty, // Null check
            ["sortByColumn"] = column.SortByColumn != null ? column.SortByColumn.Name : "None" // Null check
        };

        // Add calculated column metadata
        if (column is CalculatedColumn calculatedColumn)
        {
            columnJson["isCalculatedColumn"] = true;
            columnJson["expression"] = calculatedColumn.Expression;
            columnJson["formatString"] = calculatedColumn.FormatString ?? string.Empty; // Null check
        }
        else
        {
            columnJson["isCalculatedColumn"] = false;
        }
        columns.Add(columnJson);
    }
    tableJson["columns"] = columns;

    // Add measures to the table metadata
    var measures = new JArray();
    foreach (var measure in table.Measures)
    {
        var measureJson = new JObject
        {
            ["name"] = measure.Name,
            ["description"] = measure.Description ?? string.Empty, // Null check
            ["expression"] = measure.Expression,
            ["formatString"] = measure.FormatString ?? string.Empty, // Null check
            ["isHidden"] = measure.IsHidden,
            ["displayFolder"] = measure.DisplayFolder ?? string.Empty // Null check
        };
        measures.Add(measureJson);
    }
    tableJson["measures"] = measures;

    return tableJson;
}

// Helper function to get metadata for calculation groups
JObject GetCalculationGroupMetadata(CalculationGroupTable calcGroupTable)
{
    var calcGroupJson = new JObject
    {
        ["name"] = calcGroupTable.Name,
        ["description"] = calcGroupTable.Description ?? string.Empty // Null check
    };

    // Add calculation items to the calculation group metadata
    var calcItems = new JArray();
    foreach (var item in calcGroupTable.CalculationItems)
    {
        var itemJson = new JObject
        {
            ["name"] = item.Name,
            ["expression"] = item.Expression
        };
        calcItems.Add(itemJson);
    }
    calcGroupJson["calculationItems"] = calcItems;

    // Add measures to the calculation group metadata
    var calcGroupMeasures = new JArray();
    foreach (var measure in calcGroupTable.Measures)
    {
        var measureJson = new JObject
        {
            ["name"] = measure.Name,
            ["description"] = measure.Description ?? string.Empty, // Null check
            ["expression"] = measure.Expression,
            ["formatString"] = measure.FormatString ?? string.Empty, // Null check
            ["isHidden"] = measure.IsHidden,
            ["displayFolder"] = measure.DisplayFolder ?? string.Empty // Null check
        };
        calcGroupMeasures.Add(measureJson);
    }
    calcGroupJson["measures"] = calcGroupMeasures;

    // Add calculated columns to the calculation group
    var calcColumns = new JArray();
    foreach (var column in calcGroupTable.Columns)
    {
        if (column is CalculatedColumn calculatedColumn)
        {
            var columnJson = new JObject
            {
                ["name"] = column.Name,
                ["dataType"] = column.DataType.ToString(),
                ["dataCategory"] = column.DataCategory?.ToString() ?? "Uncategorized", // Null check
                ["description"] = column.Description ?? string.Empty, // Null check
                ["isHidden"] = column.IsHidden,
                ["formatString"] = column.FormatString ?? string.Empty, // Null check
                ["displayFolder"] = column.DisplayFolder ?? string.Empty, // Null check
                ["sortByColumn"] = column.SortByColumn != null ? column.SortByColumn.Name : "None" // Null check
            };
            calcColumns.Add(columnJson);
        }
    }
    calcGroupJson["calculatedColumns"] = calcColumns;

    return calcGroupJson;
}

// Optional function to save the DataModel.json to disk
void SaveDataModelToFile(string jsonContent)
{
    try
    {
        // Write the JSON content to the file
        System.IO.File.WriteAllText(filePath, jsonContent);
        Info($"DataModel.json has been saved to {filePath}");
    }
    catch (System.Exception ex)
    {
        Error($"Failed to save DataModel.json: {ex.Message}");
    }
}

string GetFileIdByName(System.Net.Http.HttpClient client, string baseUrl, string apiKey, string fileNameToFind)
{
    // Set the authorization header
    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

    // Make the GET request to fetch the list of files
    var response = client.GetAsync($"{baseUrl}/files").Result;

    if (response.IsSuccessStatusCode)
    {
        var content = response.Content.ReadAsStringAsync().Result;
        var filesData = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(content);

        // Search for the file by name
        foreach (var file in filesData["data"])
        {
            if (file["filename"].ToString() == fileNameToFind)
            {
                return file["id"].ToString(); // Return the file ID if found
            }
        }

        return ""; // Return empty string if file not found
    }
    else
    {
        Error($"Error: {response.StatusCode}, {response.ReasonPhrase}");
        return "";
    }
}
