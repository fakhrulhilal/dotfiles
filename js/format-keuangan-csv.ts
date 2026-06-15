function main(workbook: ExcelScript.Workbook) {
	let selectedRange = workbook.getSelectedRange();
	const table = workbook.addTable(selectedRange, false);
	table.setPredefinedTableStyle("TableStyleMedium7");

	table.addColumn(0); table.addColumn(-1);
	table.getHeaderRowRange().setValues([["kategori", "tanggal", "keterangan", "nominal", "plain"]]);

	configureColumn(table, "kategori", 10, Formats.text, Styles.shortText);
	configureColumn(table, "tanggal", 10, Formats.tanggal, Styles.shortText);
	configureColumn(table, "keterangan", 50, Formats.text, Styles.longText);
	configureColumn(table, "nominal", 10, Formats.uang, Styles.number);
	configureColumn(table, "plain", 60, Formats.text, Styles.longText);

	table.getColumnByName("plain")
		.getRangeBetweenHeaderAndTotal()
		.setFormula(
			`=TEXT([@tanggal],"d MMM")&": "&[@keterangan]&" = "&IFS(MOD([@nominal],1000)=0,TEXT([@nominal]/1000,"0")&"k",AND(MOD([@nominal],1000)>0,MOD([@nominal],100)>0),TEXT([@nominal],"0.#00"),TRUE,TEXT([@nominal]/1000,"0,#k"))&"; "`
		);
}

function configureColumn(
	table: ExcelScript.Table,
	identifier: number | string,
	width: number,
	format: string,
	style: (fmt: ExcelScript.RangeFormat) => void) {
	const column = table.getColumn(identifier);
	if (typeof identifier === 'string') column.setName(identifier);

	const columnContent = column.getRange().getEntireColumn();
	const columnFormat = columnContent.getFormat();
	columnFormat.setColumnWidth(width * 6 + 5);
	style(columnFormat);
	if (format?.length > 0) columnContent.setNumberFormatLocal(format);
}

const Styles = {
	shortText: (column: ExcelScript.RangeFormat) => {
		column.setHorizontalAlignment(ExcelScript.HorizontalAlignment.left);
		column.setVerticalAlignment(ExcelScript.VerticalAlignment.top);
	},
	longText: (column: ExcelScript.RangeFormat) => {
		column.setHorizontalAlignment(ExcelScript.HorizontalAlignment.justify);
		column.setVerticalAlignment(ExcelScript.VerticalAlignment.top);
		column.setWrapText(true);
	},
	"number": (column: ExcelScript.RangeFormat) => {
		column.setHorizontalAlignment(ExcelScript.HorizontalAlignment.right);
		column.setVerticalAlignment(ExcelScript.VerticalAlignment.top);
	},
}

const enum Formats {
	tanggal = "[$-id-ID]dd-mmm-yy;@",
	uang = "#.##0",
	text = "",
}
