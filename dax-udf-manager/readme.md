# TE3 GitHub UDF Manager – C# Script for Tabular Editor 3

## Features

### Organized Tree View of the GitHub Repository

All *.dax files (DAX UDF functions) are grouped by folder, based on their GitHub path.

You can expand or collapse folders and select one or multiple functions.

**Bold font** → The function already exists in your current model.  
**Regular font** → The function exists only in the GitHub repository.

### Compare Model Functions with GitHub

Click the "Compare" button to check whether functions in your model match their GitHub versions.

**Green font** → Code is identical between the model and GitHub.  
**Red font** → Code differs between the model and GitHub.

### Load or Update DAX Functions in Your Model

Browse and select the desired DAX UDFs from the GitHub repository.

Click the "Update in the Model" button to load new functions or update existing functions in the model with the latest GitHub version.

### Create New Functions in GitHub

Click the "Create in GitHub" button to add new .dax files to the GitHub repository using the selected functions from your model.

You can choose the destination folder before creating the new files.

### Push Changes to GitHub

Click the "Update in GitHub" button to update .dax files in the GitHub repository using the function definitions from your model.

This action only updates existing functions — it does not create new .dax files.

### Authentication

**GitHub Token Required**

Set your GitHub token as an environment variable: `GITHUB_TOKEN` (User or Machine level).

This is required for accessing both public and private repositories.

To start using the tool, copy the C# code into Tabular Editor and save it as a new macro. Then, create a GitHub access token and save it as an environment variable. Update the GitHub repo parameters if needed.

<img width="666" height="713" alt="image" src="https://github.com/user-attachments/assets/ef6ba5a0-ca50-4ebe-b837-c8a80efffece" />
