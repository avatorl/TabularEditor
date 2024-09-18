using System.Windows.Forms;

// Create a new form
Form form = new Form();

// Set the form's top and left position directly
form.Top = 300;  // pixels from the top of the screen
form.Left = 300; // pixels from the left of the screen

ComboBox comboBox = new ComboBox();
Button submitButton = new Button();

string svg1 = "\n<rect x='' y='' x2='' y2='' style='stroke: black; fill: none;' />";
string svg2 = "\n<circle cx='' cy='' r='' style='stroke: black; fill: none;' />";

form.Text = "Select SVG Element";
comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
comboBox.Items.AddRange(new string[] { svg1, svg2 }); // Add your parameter values here
submitButton.Text = "OK";
submitButton.DialogResult = DialogResult.OK;

comboBox.SetBounds(10, 10, 300, 20);
submitButton.SetBounds(310, 10, 75, 20);

form.ClientSize = new System.Drawing.Size(400, 40);
form.Controls.AddRange(new Control[] { comboBox, submitButton });
form.FormBorderStyle = FormBorderStyle.FixedDialog;
form.StartPosition = FormStartPosition.Manual; // Ensure the form uses the Top and Left properties for positioning
form.MinimizeBox = false;
form.MaximizeBox = false;
form.AcceptButton = submitButton;

// Show form as a dialog and check the result
if (form.ShowDialog() == DialogResult.OK)
{
    string selectedValue = comboBox.SelectedItem.ToString();

    if (Selected.Measures.Count == 1)
    {
        var measure = Selected.Measures.FirstOrDefault();

        if (measure != null)
        {
            // Define the additional calculation using the selected value
            string additionalCalculation = $"+{selectedValue}";

            // Append the additional calculation to the measure's existing expression
            measure.Expression += additionalCalculation;
        }
    }
    else
    {
        MessageBox.Show("Please select exactly one measure to modify.");
    }
}
else
{
    MessageBox.Show("No parameter value selected. The measure will not be modified.");
}
