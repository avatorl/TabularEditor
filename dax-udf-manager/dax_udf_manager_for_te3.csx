// ===========================================================================
// TE3 Macro: GitHub DAX UDF Manager for TE3
// ---------------------------------------------------------------------------
// Name:
//   GitHub DAX UDF Manager for TE3
//
// Purpose:
//   Manage DAX user-defined functions (UDFs) between Tabular Editor 3 and a
//   GitHub repository. Load/compare/update/create UDF files with clear visual
//   indicators and safe, user-friendly workflows.
//
// Requirements:
//   - GitHub personal access token (classic or fine-grained) in environment
//     variable GITHUB_TOKEN (User or Machine scope).
//
// Links:
//   - Project repo: https://github.com/avatorl/DAX/tree/master/UDF/_TE3
//   - Blog: https://powerofbi.org/
//   - LinkedIn: https://www.linkedin.com/in/avatorl/
//
// ===========================================================================


// ===========================================================================
// Repository configuration
// These define the GitHub repo location used by this macro
// ===========================================================================
var owner  = "avatorl";
var repo   = "DAX";
var folder = "UDF";
var branch = "master";


// ===========================================================================
// Small helpers (inline) to keep the script tidy
// ===========================================================================
Func<string, string> UrlEncode = s => System.Uri.EscapeDataString(s ?? "");

// Helper to trim long strings (e.g., error messages)
Func<string, string> Truncate = (text) =>
{
    if (string.IsNullOrEmpty(text)) return "";
    return text.Length <= 300 ? text : text.Substring(0, 300) + "...";
};

// Helper to temporarily show a Wait cursor during long operations
Action<System.Windows.Forms.Control, System.Action> WithWaitCursor = (ctl, action) =>
{
    var old = ctl.Cursor;
    try
    {
        ctl.Cursor = System.Windows.Forms.Cursors.WaitCursor;
        action();
    }
    finally
    {
        ctl.Cursor = old;
    }
};


// ===========================================================================
// GitHub URL builders (centralized templates)
// ===========================================================================
Func<string, string> GetApiUrl = (path) =>
    $"https://api.github.com/repos/{owner}/{repo}/contents/{UrlEncode(path)}?ref={UrlEncode(branch)}";

Func<string, string> GetUploadUrl = (path) =>
    $"https://api.github.com/repos/{owner}/{repo}/contents/{UrlEncode(path)}";

Func<string> GetBrowserUrl = () =>
    $"https://github.com/{owner}/{repo}/tree/{branch}/{folder}";

// Build a normalized file path from repo object
Func<dynamic, string> GetFilePath = (f) =>
    string.IsNullOrEmpty(f.Path)
        ? $"{folder}/{f.Name}.dax"
        : $"{folder}/{f.Path.Replace('\\','/').Trim('/')}/{f.Name}.dax";


// ===========================================================================
// GitHub Token (required)
// Reads token from environment variables, else aborts
// ===========================================================================
var githubToken = System.Environment.GetEnvironmentVariable("GITHUB_TOKEN", System.EnvironmentVariableTarget.User)
                ?? System.Environment.GetEnvironmentVariable("GITHUB_TOKEN", System.EnvironmentVariableTarget.Machine);

// Abort if token is missing
if (string.IsNullOrEmpty(githubToken))
{
    System.Windows.Forms.MessageBox.Show("⚠️ Missing GitHub token. Please set GITHUB_TOKEN as an environment variable.");
    return;
}


// ===========================================================================
// HttpClient (shared instance for all requests)
// Always reuse single HttpClient for performance
// ===========================================================================
var client = new System.Net.Http.HttpClient();
client.DefaultRequestHeaders.Clear();
client.DefaultRequestHeaders.Add("User-Agent", "TabularEditor3");
client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
client.DefaultRequestHeaders.Add("Authorization", $"token {githubToken}");


// ===========================================================================
// Helper: Normalize line endings
// Ensures consistent formatting of UDF code before comparing/uploading
// ===========================================================================
Func<string, string> NormalizeLineEndings = text =>
{
    if (string.IsNullOrEmpty(text)) return "";

    // Normalize CRLF/CR → LF
    var norm = text.Replace("\r\n", "\n").Replace("\r", "\n");

    // Trim trailing spaces on each line (but keep leading newlines intact)
    norm = string.Join("\n", norm.Split('\n').Select(line => line.TrimEnd()));

    // Ensure trailing newline for consistency
    if (!norm.EndsWith("\n")) norm += "\n";
    return norm;
};


// ===========================================================================
// Helper: Recursively scan repo for .dax files
// Returns a list of objects with { Name, Url, Path }
// ===========================================================================
System.Collections.Generic.List<dynamic> GetFuncs(string apiUrl, string relPath)
{
    var list = new System.Collections.Generic.List<dynamic>();
    try
    {
        // Call GitHub API
        var responseText = client.GetStringAsync(apiUrl).Result;
        var items = Newtonsoft.Json.Linq.JArray.Parse(responseText);

        // Loop over each item returned by GitHub
        foreach (var item in items)
        {
            var type = item["type"]?.ToString();
            var name = item["name"]?.ToString() ?? "";

            // If directory → recurse deeper
            if (type == "dir")
            {
                var nextApi = item["url"]?.ToString();
                var nextRel = string.IsNullOrEmpty(relPath) ? name : $"{relPath}/{name}";
                list.AddRange(GetFuncs(nextApi, nextRel));
            }
            // If file ends with .dax → capture metadata
            else if (type == "file" && name.EndsWith(".dax", System.StringComparison.OrdinalIgnoreCase))
            {
                list.Add(new {
                    Name = System.IO.Path.GetFileNameWithoutExtension(name),
                    Url  = item["download_url"]?.ToString(),
                    Path = relPath // keep with forward slashes
                });
            }
        }
    }
    catch (System.Exception ex)
    {
        // On failure, show error message
        System.Diagnostics.Debug.WriteLine("GetFuncs error: " + ex);
        System.Windows.Forms.MessageBox.Show($"Error scanning repo: {ex.Message}");
    }

    return list;
}


// ===========================================================================
// Helper: Upload updated/new file to GitHub
// Handles both create and update scenarios
// ===========================================================================
Action<string,string,string> UploadToGitHub = (path, code, sha) =>
{
    try
    {
        // Normalize code before upload
        var normalized = NormalizeLineEndings(code);
        var url = GetUploadUrl(path);

        // Request body for GitHub API
        var body = new {
            message = $"Update UDF '{System.IO.Path.GetFileNameWithoutExtension(path)}' via GitHub DAX UDF Manager for TE3",
            content = System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(normalized)),
            branch  = branch,
            sha     = sha
        };

        var json = Newtonsoft.Json.JsonConvert.SerializeObject(body);
        var resp = client.PutAsync(url, new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json")).Result;
        var respText = resp.Content.ReadAsStringAsync().Result;

        // Throw if GitHub rejected the update
        if (!resp.IsSuccessStatusCode)
            throw new System.Exception($"{(int)resp.StatusCode} {resp.ReasonPhrase}: {Truncate(respText)}");
    }
    catch (System.Exception ex)
    {
        // Show friendly error
        System.Diagnostics.Debug.WriteLine("UploadToGitHub error: " + ex);
        System.Windows.Forms.MessageBox.Show($"Error uploading to GitHub: {ex.Message}");
    }
};


// ===========================================================================
// UI: Form, Layout, TreeView
// ===========================================================================
var form = new System.Windows.Forms.Form();
form.Text = "GitHub DAX UDF Manager for TE3";
form.Width = 680;
form.Height = 720;
form.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
form.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;

// TableLayout with 3 rows: TreeView (fill), Legend (auto), Buttons (auto)
var layout = new System.Windows.Forms.TableLayoutPanel();
layout.Dock = System.Windows.Forms.DockStyle.Fill;
layout.ColumnCount = 1;
layout.RowCount = 3;
layout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
layout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
layout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
form.Controls.Add(layout);

// TreeView: displays repo UDFs and model-only UDFs
var tree = new System.Windows.Forms.TreeView();
tree.CheckBoxes = true;         // allow selection
tree.Dock = System.Windows.Forms.DockStyle.Fill;
tree.HideSelection = false;     // keep selection visible when focus lost
tree.FullRowSelect = true;      // select entire row
tree.ShowNodeToolTips = true;   // show tooltips on hover
layout.Controls.Add(tree, 0, 0);


// ===========================================================================
// UI: Legend Panel (status explanation)
// ===========================================================================
var legend = new System.Windows.Forms.FlowLayoutPanel();
legend.AutoSize = true;
legend.Dock = System.Windows.Forms.DockStyle.Fill;
legend.WrapContents = false;
legend.Padding = new System.Windows.Forms.Padding(8, 6, 8, 6);

// Helper to add legend items with consistent style
void AddLegendItem(string text, System.Drawing.Color color, System.Drawing.FontStyle style)
{
    var lbl = new System.Windows.Forms.Label();
    lbl.Text = text;
    lbl.AutoSize = true;
    lbl.Margin = new System.Windows.Forms.Padding(8, 4, 8, 4);
    lbl.ForeColor = color;
    lbl.Font = new System.Drawing.Font(tree.Font, style);
    legend.Controls.Add(lbl);
}

// Legend items: explains node colors/fonts
AddLegendItem("Legend: ", System.Drawing.Color.Black, System.Drawing.FontStyle.Regular);
AddLegendItem("not in model", System.Drawing.Color.Black, System.Drawing.FontStyle.Regular);
AddLegendItem("exists in model", System.Drawing.Color.Black, System.Drawing.FontStyle.Bold);
AddLegendItem("match", System.Drawing.Color.Green, System.Drawing.FontStyle.Bold);
AddLegendItem("differs", System.Drawing.Color.Red, System.Drawing.FontStyle.Bold);
AddLegendItem("deleted", System.Drawing.Color.DarkOrange, System.Drawing.FontStyle.Bold);
AddLegendItem("model-only UDF", System.Drawing.Color.Blue, System.Drawing.FontStyle.Bold);

layout.Controls.Add(legend, 0, 1);


// ===========================================================================
// UI: Buttons (FlowLayout right-aligned)
// ===========================================================================
var buttons = new System.Windows.Forms.FlowLayoutPanel();
buttons.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
buttons.Dock = System.Windows.Forms.DockStyle.Fill;
buttons.AutoSize = true;
buttons.Padding = new System.Windows.Forms.Padding(0, 8, 8, 8); // spacing

// Define all action buttons
var compare     = new System.Windows.Forms.Button { Text="Compare", AutoSize = true };
var updateModel = new System.Windows.Forms.Button { Text="Update in the Model", AutoSize = true };
var updateGitHub= new System.Windows.Forms.Button { Text="Update in GitHub", AutoSize = true };
var createGitHub= new System.Windows.Forms.Button { Text="Create in GitHub", AutoSize = true };
var openGitHub  = new System.Windows.Forms.Button { Text="Open GitHub", AutoSize = true };
var cancel      = new System.Windows.Forms.Button { Text="Close", AutoSize = true, DialogResult = System.Windows.Forms.DialogResult.Cancel };

// Add in reverse order so Close ends on far right
buttons.Controls.Add(cancel);
buttons.Controls.Add(openGitHub);
buttons.Controls.Add(createGitHub);
buttons.Controls.Add(updateGitHub);
buttons.Controls.Add(updateModel);
buttons.Controls.Add(compare);

// Ensure even spacing between all buttons
foreach (System.Windows.Forms.Control ctrl in buttons.Controls)
{
    ctrl.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
}

layout.Controls.Add(buttons, 0, 2);
form.CancelButton = cancel; // Esc key closes form


// ===========================================================================
// Helper: Build/refresh tree (repo + model-only UDFs)
// ===========================================================================
Action RefreshTree = () =>
{
    WithWaitCursor(form, () =>
    {
        // Load UDFs from GitHub
        var funcs = GetFuncs(GetApiUrl(folder), "");

        // Collect model function names for comparison
        var existing = new System.Collections.Generic.HashSet<string>(
            Model.Functions.Select(f => f.Name),
            System.StringComparer.OrdinalIgnoreCase
        );

        tree.BeginUpdate(); // freeze redraw for performance
        try
        {
            tree.Nodes.Clear();
            var root = new System.Windows.Forms.TreeNode($"{owner}/{repo}/{folder}");
            tree.Nodes.Add(root);

            // Helper to find existing node or create new one
            System.Windows.Forms.TreeNode GetOrCreateNode(System.Windows.Forms.TreeNodeCollection nodes, string name)
            {
                foreach (System.Windows.Forms.TreeNode n in nodes)
                    if (n.Text == name) return n;
                var newNode = new System.Windows.Forms.TreeNode(name);
                nodes.Add(newNode);
                return newNode;
            }

            // Build repo branch with folder hierarchy
            foreach (var f in funcs.OrderBy(x => x.Path).ThenBy(x => x.Name))
            {
                var pathParts = string.IsNullOrEmpty(f.Path)
                    ? System.Array.Empty<string>()
                    : f.Path.Split(new[] { '/' }, System.StringSplitOptions.RemoveEmptyEntries);

                var current = root.Nodes;
                foreach (var part in pathParts)
                    current = GetOrCreateNode(current, part).Nodes;

                var node = new System.Windows.Forms.TreeNode(f.Name) { Tag = f };

                // Bold font if function exists in model
                if (existing.Contains(f.Name))
                    node.NodeFont = new System.Drawing.Font(tree.Font, System.Drawing.FontStyle.Bold);

                current.Add(node);
            }

            // Build model-only branch
            var modelOnlyRoot = new System.Windows.Forms.TreeNode("Model-only UDFs");
            tree.Nodes.Add(modelOnlyRoot);

            // For each model UDF not found in repo → mark as blue
            foreach (var fn in Model.Functions)
            {
                if (!funcs.Any(f => string.Equals(f.Name, fn.Name, System.StringComparison.OrdinalIgnoreCase)))
                {
                    var node = new System.Windows.Forms.TreeNode(fn.Name) { Tag = fn };
                    node.NodeFont = new System.Drawing.Font(tree.Font, System.Drawing.FontStyle.Bold);
                    node.ForeColor = System.Drawing.Color.Blue;
                    modelOnlyRoot.Nodes.Add(node);
                }
            }

            tree.ExpandAll();
        }
        finally
        {
            tree.EndUpdate();
        }
    });
};

// Initial load of tree
RefreshTree();


// ===========================================================================
// Helper: Traverse tree recursively
// Applies an action to every node in the tree
// ===========================================================================
void TraverseNodes(System.Windows.Forms.TreeNodeCollection nodes, System.Action<System.Windows.Forms.TreeNode> action)
{
    foreach (System.Windows.Forms.TreeNode node in nodes)
    {
        action(node);

        // Recurse into child nodes if present
        if (node.Nodes.Count > 0)
            TraverseNodes(node.Nodes, action);
    }
}


// ===========================================================================
// Button: Open GitHub in browser
// ===========================================================================
openGitHub.Click += (s, e) =>
{
    try
    {
        // Open repo URL in default browser
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = GetBrowserUrl(),
            UseShellExecute = true
        });
    }
    catch (System.Exception ex)
    {
        // Show error if opening fails
        System.Diagnostics.Debug.WriteLine("Open GitHub error: " + ex);
        System.Windows.Forms.MessageBox.Show($"Error opening GitHub: {ex.Message}");
    }
};


// ===========================================================================
// Button: Create GitHub files for model-only UDFs
// Includes overwrite confirmation if file already exists
// ===========================================================================
createGitHub.Click += (s, e) =>
{
    // Collect all selected model-only UDFs
    var selected = new System.Collections.Generic.List<TabularEditor.TOMWrapper.Function>();
    TraverseNodes(tree.Nodes, node =>
    {
        if (node.Checked && node.Tag is TabularEditor.TOMWrapper.Function fn)
            selected.Add(fn);
    });

    if (selected.Count == 0)
    {
        System.Windows.Forms.MessageBox.Show("No model-only UDFs selected.");
        return;
    }

    // ============================
    // Build Folder Picker dialog
    // ============================
    var folderForm = new System.Windows.Forms.Form();
    folderForm.Text = "Select GitHub Destination Folder";
    folderForm.Width = 400;
    folderForm.Height = 500;
    folderForm.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;

    var folderTree = new System.Windows.Forms.TreeView();
    folderTree.Dock = System.Windows.Forms.DockStyle.Fill;
    folderTree.FullRowSelect = true;
    folderTree.HideSelection = false;

    var okButton = new System.Windows.Forms.Button { Text = "OK", Dock = System.Windows.Forms.DockStyle.Bottom, DialogResult = System.Windows.Forms.DialogResult.OK };
    folderForm.Controls.Add(folderTree);
    folderForm.Controls.Add(okButton);
    folderForm.AcceptButton = okButton;

 // Helper: Recursively load folder structure from GitHub
Action<string, string, System.Windows.Forms.TreeNode> LoadFolders = null;
LoadFolders = (apiUrl, relPath, parentNode) =>
{
    try
    {
        var responseText = client.GetStringAsync(apiUrl).Result;
        var items = Newtonsoft.Json.Linq.JArray.Parse(responseText);

        foreach (var item in items)
        {
            var type = item["type"]?.ToString();
            var name = item["name"]?.ToString() ?? "";

            if (type == "dir")
            {
                var nextApi = item["url"]?.ToString();
                var nextRel = string.IsNullOrEmpty(relPath) ? name : $"{relPath}/{name}";

                var dirNode = new System.Windows.Forms.TreeNode(name);
                dirNode.Tag = nextRel;

                parentNode.Nodes.Add(dirNode);

                // ❌ Wrong: dirNode.BeforeExpand (doesn't exist)
                // ✅ Instead, load recursively now
                LoadFolders(nextApi, nextRel, dirNode);
            }
        }
    }
    catch (System.Exception ex)
    {
        System.Diagnostics.Debug.WriteLine("LoadFolders error: " + ex);
    }
};


    // Root node for the repo folder
    var rootNode = new System.Windows.Forms.TreeNode($"{owner}/{repo}/{folder}") { Tag = folder };
    folderTree.Nodes.Add(rootNode);
    LoadFolders(GetApiUrl(folder), folder, rootNode);
    rootNode.Expand();

    // Show dialog
    string chosenFolder = null;
    if (folderForm.ShowDialog(form) == System.Windows.Forms.DialogResult.OK && folderTree.SelectedNode != null)
    {
        chosenFolder = folderTree.SelectedNode.Tag as string;
    }

    if (string.IsNullOrEmpty(chosenFolder))
    {
        System.Windows.Forms.MessageBox.Show("No folder selected. Aborting.");
        return;
    }

    // ============================
    // Create files in GitHub
    // ============================
    WithWaitCursor(form, () =>
    {
        int createdOrUpdated = 0;

        foreach (var fn in selected)
        {
            try
            {
                var path = $"{chosenFolder}/{fn.Name}.dax";
                string sha = null;

                // Check if file already exists in GitHub
                var checkResp = client.GetAsync(GetApiUrl(path)).Result;
                if (checkResp.IsSuccessStatusCode)
                {
                    var obj = Newtonsoft.Json.Linq.JObject.Parse(checkResp.Content.ReadAsStringAsync().Result);
                    sha = (string)obj["sha"];

                    // Ask overwrite
                    var ans = System.Windows.Forms.MessageBox.Show(
                        $"'{fn.Name}.dax' already exists in GitHub at {chosenFolder}.\nDo you want to overwrite it?",
                        "File exists",
                        System.Windows.Forms.MessageBoxButtons.YesNoCancel,
                        System.Windows.Forms.MessageBoxIcon.Question);

                    if (ans == System.Windows.Forms.DialogResult.Cancel) return; 
                    if (ans == System.Windows.Forms.DialogResult.No) continue;
                }

                // Upload
                var normalized = NormalizeLineEndings(fn.Expression);
                UploadToGitHub(path, normalized, sha);
                createdOrUpdated++;
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("CreateGitHub error: " + ex);
                System.Windows.Forms.MessageBox.Show($"Error creating {fn.Name}: {ex.Message}");
            }
        }

        RefreshTree();
        System.Windows.Forms.MessageBox.Show($"Created/updated {createdOrUpdated} UDF(s) in GitHub.");
    });
};



// ===========================================================================
// Button: Compare model vs GitHub
// Marks matches (green), diffs (red), errors (orange)
// ===========================================================================
compare.Click += (s, e) =>
{
    WithWaitCursor(form, () =>
    {
        var existingNow = new System.Collections.Generic.HashSet<string>(
            Model.Functions.Select(fn => fn.Name),
            System.StringComparer.OrdinalIgnoreCase
        );

        var contentCache = new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

        TraverseNodes(tree.Nodes, node =>
        {
            if (node.Tag == null) return;

            // If this is a model-only UDF, keep it blue and skip compare
            if (node.Tag is TabularEditor.TOMWrapper.Function)
            {
                node.ForeColor = System.Drawing.Color.Blue;
                node.ToolTipText = "Exists only in model (not in GitHub)";
                return;
            }

            // Reset defaults only for repo nodes
            node.ForeColor = System.Drawing.Color.Black;
            node.ToolTipText = "";

            var f = (dynamic)node.Tag;

            // Compare only if this GitHub UDF exists in the model
            if (existingNow.Contains((string)f.Name))
            {
                try
                {
                    var apiPath = GetFilePath(f);
                    string code;

                    if (!contentCache.TryGetValue(apiPath, out code))
                    {
                        var json = client.GetStringAsync(GetApiUrl(apiPath)).Result;
                        var obj = Newtonsoft.Json.Linq.JObject.Parse(json);
                        var base64 = (string)obj["content"];
                        code = System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(base64));
                        contentCache[apiPath] = code;
                    }

                    var modelFn = Model.Functions
                        .FirstOrDefault(fn => fn.Name.Equals(f.Name, System.StringComparison.OrdinalIgnoreCase));

                    if (modelFn != null)
                    {
                        var repoCode  = NormalizeLineEndings(code);
                        var modelCode = NormalizeLineEndings(modelFn.Expression);

                        if (modelCode == repoCode)
                        {
                            node.ForeColor = System.Drawing.Color.Green;
                            node.ToolTipText = "Match between model and GitHub";
                        }
                        else
                        {
                            node.ForeColor = System.Drawing.Color.Red;
                            node.ToolTipText = "Code differs between model and GitHub";
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    node.ForeColor = System.Drawing.Color.DarkOrange;
                    node.ToolTipText = $"Error comparing: {ex.Message}";
                    System.Diagnostics.Debug.WriteLine("Compare error: " + ex);
                }
            }
        });
    });
};


// ===========================================================================
// Button: Update GitHub with selected model functions
// Pushes changes from model → GitHub
// ===========================================================================
updateGitHub.Click += (s, e) =>
{
    // Collect selected UDFs (both repo + model-only)
    var selected = new System.Collections.Generic.List<dynamic>();
    TraverseNodes(tree.Nodes, node =>
    {
        if (node.Checked && node.Tag != null)
            selected.Add((dynamic)node.Tag);
    });

    // Abort if none selected
    if (selected.Count == 0)
    {
        System.Windows.Forms.MessageBox.Show("No UDFs selected.");
        return;
    }

    WithWaitCursor(form, () =>
    {
        int updated = 0;

        foreach (var f in selected)
        {
            try
            {
                var path = GetFilePath(f);
                string sha = null;

                // Fetch existing SHA if file exists
                var resp = client.GetAsync(GetApiUrl(path)).Result;
                if (resp.IsSuccessStatusCode)
                {
                    var obj = Newtonsoft.Json.Linq.JObject.Parse(resp.Content.ReadAsStringAsync().Result);
                    sha = (string)obj["sha"];
                }

                // Find function in model
                var fn = Model.Functions.FirstOrDefault(x => x.Name.Equals(f.Name, System.StringComparison.OrdinalIgnoreCase));
                if (fn != null)
                {
                    var normalized = NormalizeLineEndings(fn.Expression);

                    // Extract description from first comment line (if any)
                    var lines = normalized.Split(new[] { '\n' }, System.StringSplitOptions.None);
                    if (lines.Length > 0 && lines[0].TrimStart().StartsWith("//"))
                        fn.Description = lines[0].Trim().Substring(2).Trim();

                    // Upload normalized expression to GitHub
                    UploadToGitHub(path, normalized, sha);

                    // Keep model expression normalized
                    fn.Expression = normalized;

                    updated++;
                }
            }
            catch (System.Exception ex)
            {
                // Error per UDF
                System.Diagnostics.Debug.WriteLine("UpdateGitHub error: " + ex);
                System.Windows.Forms.MessageBox.Show($"Error updating {f.Name}: {ex.Message}");
            }
        }

        // Refresh and show summary
        RefreshTree();
        System.Windows.Forms.MessageBox.Show($"Updated {updated} UDF(s) in GitHub.");
    });
};


// ===========================================================================
// Button: Update Model from GitHub
// Pulls changes from GitHub → model
// ===========================================================================
updateModel.Click += (s, e) =>
{
    // Collect selected UDFs
    var selected = new System.Collections.Generic.List<dynamic>();
    TraverseNodes(tree.Nodes, node =>
    {
        if (node.Checked && node.Tag != null)
            selected.Add((dynamic)node.Tag);
    });

    // Abort if none selected
    if (selected.Count == 0)
    {
        System.Windows.Forms.MessageBox.Show("No UDFs selected.");
        return;
    }

    WithWaitCursor(form, () =>
    {
        int updated = 0;

        // Process each selected function
        foreach (var f in selected)
        {
            try
            {
                var path = GetFilePath(f);

                // Download file content from GitHub
                var json = client.GetStringAsync(GetApiUrl(path)).Result;
                var obj = Newtonsoft.Json.Linq.JObject.Parse(json);
                var base64 = (string)obj["content"];
                var code = System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(base64));
                var normalized = NormalizeLineEndings(code);

                // Extract description from first comment line
                string desc = null;
                var lines = normalized.Split(new[] { '\n' }, System.StringSplitOptions.None);
                if (lines.Length > 0 && lines[0].TrimStart().StartsWith("//"))
                    desc = lines[0].Trim().Substring(2).Trim();

                // Add or update function in model
                var fn = Model.Functions.FirstOrDefault(x => x.Name.Equals(f.Name, System.StringComparison.OrdinalIgnoreCase));
                if (fn == null)
                {
                    fn = Model.AddFunction(f.Name, normalized);
                    if (!string.IsNullOrEmpty(desc)) fn.Description = desc;
                }
                else
                {
                    fn.Expression = normalized;
                    if (!string.IsNullOrEmpty(desc)) fn.Description = desc;
                }

                updated++;
            }
            catch (System.Exception ex)
            {
                // Error per function
                System.Diagnostics.Debug.WriteLine("UpdateModel error: " + ex);
                System.Windows.Forms.MessageBox.Show($"Error updating model with {f.Name}: {ex.Message}");
            }
        }

        // Show summary
        System.Windows.Forms.MessageBox.Show($"Updated {updated} UDF(s) in the Model.");
    });
};


// ===========================================================================
// Run the form (main UI loop)
// ===========================================================================
form.ShowDialog();
