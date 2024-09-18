// for all Int64 columns set EncodingHint
foreach(var column in Model.Tables.SelectMany(t => t.Columns))
{
if(column.DataType == DataType.Int64)
column.EncodingHint = EncodingHintType.Value;
}
