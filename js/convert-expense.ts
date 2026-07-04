#!/usr/bin/env -S bun run

import { traceInvocation } from "./otlp";
import { Command, ValidationError } from "@cliffy/command";
import { HelpCommand } from "@cliffy/command/help";
import { CompletionsCommand } from "@cliffy/command/completions";
import { Workbook, Worksheet, Table } from "documonster/excel";
import { Csv } from "documonster/csv";
import { Span, trace } from "@opentelemetry/api";
import { basename, extname } from "node:path";
import { existsSync } from "node:fs";
import { readFile } from "node:fs/promises";

const Config = {
    name : "convert-expense",
    description : "Process expense dari/ke Excel",
    version : "0.1.0",
};
const tracer = trace.getTracer(Config.name, Config.version);

async function importCommandHandler(span: Span, input: string, output: string | undefined, force: boolean) {
    if (!input || !existsSync(input)) {
        throw new ValidationError(`Input file not found: ${input}`)
    }

    const inputPath = input!;
    const outputPath = typeof output === 'string' ? output : inputPath.replace(/\.csv$/i, '.xlsx');
    span.setAttribute("output.path.resolved", outputPath);

    // Parse CSV
    const csvText = await readFile(inputPath, 'utf-8');
    const csvContents = Csv.parse(csvText, {
        headers : true,
        trim : true,
        delimiter : ';',
        delimitersToGuess : [';', ','],
    });

    const worksheetName = basename(inputPath, extname(inputPath)).replace(/[^a-zA-Z0-9]/g, '_');
    const workbook = Workbook.create();
    if (existsSync(outputPath) && !force) {
        await Workbook.readFile(workbook, outputPath);
    }

    const worksheet = Workbook.addWorksheet(workbook, worksheetName);
    Worksheet.setColumns(worksheet, [
        { width : 10 }, // category
        { width : 10 }, // date
        { width : 50 }, // description
        { width : 10 }, // amount
        { width : 60 }, // plain
    ]);

    const plainFormula = `TEXT([date],"d MMM")&": "&[description]&" = "&IFS(MOD([amount],1000)=0,TEXT([amount]/1000,"0")&"k",AND(MOD([amount],1000)>0,MOD([amount],100)>0),TEXT([amount],"0,#00"),TRUE,TEXT([amount]/1000,"0,#k"))&"; "`;
    const tableRows = csvContents.rows.map((record) => {
        const dateString = record['date'].toString();
        const amountString = record['amount'].toString();
        const description = record['description'].toString();
        // Parse dd/MM/yyyy — JS Date constructor doesn't handle this format
        const [day, month, year] = dateString.split('/').map(Number);
        // Use Date.UTC to avoid local timezone shifting the date when Excel serializes it
        const dateVal = new Date(Date.UTC(year, month - 1, day));
        const amountNum = parseFloat(amountString.replace(/[^0-9.-]/g, ''));
        return [
            '',
            isNaN(dateVal.getTime()) ? dateString : dateVal,
            description,
            isNaN(amountNum) ? amountString : amountNum,
            { formula : plainFormula },
        ];
    });
    Table.add(worksheet, {
        name : `table_${worksheetName}`,
        ref : `A1`,
        headerRow : true,
        style : { theme : "TableStyleMedium7", showRowStripes : true },
        columns : [
            { name : "category" },
            { name : "date", style : { numFmt : "[$-id-ID]dd-mmm-yy;@" } },
            { name : "description" },
            { name : "amount", style : { numFmt : "#,##0" } },
            { name : "plain", calculatedColumnFormula : plainFormula },
        ],
        rows : tableRows,
    });

    await Workbook.writeFile(workbook, outputPath);
    span.setAttribute("output.rows", tableRows.length);
}

await new Command()
    .name(Config.name)
    .version(Config.version)
    .description(Config.description)
    .command("import", new Command()
        .description("Import expense from CSV to Excel using table format")
        .option("-i, --input <file:string>", "Input CSV file.", { required : true })
        .option("-o, --output [file:string]", "Output Excel file.")
        .option("-f, --force [force:boolean]", "Force replace if any", { default : false })
        .action(async ({ input, output, force }, ...args) =>
            traceInvocation(
                tracer,
                "CLI import", {
                    attributes : {
                        "input.path" : input,
                        "output.path" : output ?? '<unset>',
                    },
                },
                async (span) => await importCommandHandler(span, input, output, force))))
    .command("help", new HelpCommand().global())
    .command("completions", new CompletionsCommand())
    .parse();

