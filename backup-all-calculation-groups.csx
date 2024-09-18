using System.IO;

var file = @"d:\POWER BI - TOOLS & INFO\_TABULAR_SCRIPTS\Generated\Restore calculation groups (" + Model.Database.ID + ").csx";

var i = true;
var k = true;
var code ="";

using(var fileWriter = new StreamWriter(file))

{

foreach ( var cg in Model.Tables.OfType<CalculationGroupTable>() )

    {

    if ( i ) { code = "var "; } else { code = ""; }

    var output = "\n\n//create calculation group " + cg.Name;
    output = output + "\n" + code + "calculationGroupTable1 = Model.AddCalculationGroup ();";
    output = output + "\n(Model.Tables[\"New Calculation Group\"] as CalculationGroupTable).CalculationGroup.Precedence = " + cg.CalculationGroup.Precedence + ";";
    output = output + "\ncalculationGroupTable1.Name = \"" + cg.Name + "\";";

    fileWriter.Write(output);

    i = false;

    foreach (var it in cg.CalculationItems ) {

        if ( k ) { code = "var "; } else { code = ""; }

        output = "\n\n//create calculation item " + it.Name;
        output = output + "\n" + code + "calculationItem1 = calculationGroupTable1.AddCalculationItem(\"" + it.Name + "\");";
        output = output + "\ncalculationItem1.Expression = @\"" + it.Expression.Replace("\"", "\"\"")  + "\";";

        if (it.FormatStringExpression != null ) {
            output = output + "\ncalculationItem1.FormatStringExpression = @\"" + it.FormatStringExpression.Replace("\"", "\"\"")  + "\";";
        }
        output = output + "\ncalculationItem1.FormatDax();";

        fileWriter.Write(output);

        k = false;

        }

    }

} 
