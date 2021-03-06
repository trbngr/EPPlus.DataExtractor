﻿using System;
using OfficeOpenXml;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Xunit;

namespace EPPlus.DataExtractor.Tests
{
    public class WorksheetExtensionsTests
    {
        public class ItemData
        {
            public decimal MoneySpent { get; set; }

            public DateTime Date { get; set; }

            public decimal MoneyReceived { get; set; }
        }

        public class NameAndAge
        {
            public string Name { get; set; }

            public int Age { get; set; }
        }

        public class SimpleRowData
        {
            public string CarName { get; set; }

            public double Value { get; set; }

            public DateTime CreationDate { get; set; }
        }

        public class RowDataWithColumnBeingRow
        {
            public string Name { get; set; }

            public int Age { get; set; }

            public List<ColumnData> MoneyData { get; set; }
        }

        public class ColumnData
        {
            public double ReceivedMoney { get; set; }

            public DateTime Date { get; set; }
        }

        public class RowDataWithColumnBeingRowAndWithFunction : RowDataWithColumnBeingRow
        {
            public bool Is18OrOlder { get; set; }
        }

        private Stream GetSpreadsheetFileInfo() => 
            GetType().Assembly.GetManifestResourceStream(GetType(), "spreadsheets.WorkbookTest.xlsx");

        [Fact]
        public void ExtractSimpleData()
        {
            var fileInfo = GetSpreadsheetFileInfo();
            using (var package = new ExcelPackage(fileInfo))
            {
                var cars = package.Workbook.Worksheets["MainWorksheet"]
                    .Extract<SimpleRowData>()
                    .WithProperty(p => p.CarName, "B")
                    .WithProperty(p => p.Value, "C")
                    .WithProperty(p => p.CreationDate, "D")
                    .GetData(4, 6)
                    .ToList();

                Assert.Equal(3, cars.Count);

                Assert.Contains(cars,
                    i => i.CarName == "Pegeut 203" && i.Value == 20000 && i.CreationDate == new DateTime(2017, 07, 01));
                Assert.Contains(cars,
                    i => i.CarName == "i30" && i.Value == 21000 && i.CreationDate == new DateTime(2017, 04, 15));
                Assert.Contains(cars,
                    i => i.CarName == "Etios" && i.Value == 17575 && i.CreationDate == new DateTime(2015, 07, 21));
            }
        }

        [Fact]
        public void ExtractSimpleData_UseCastedValueCallback()
        {
            var fileInfo = GetSpreadsheetFileInfo();
            using (var package = new ExcelPackage(fileInfo))
            {
                var validations = new List<string>();

                var people = package.Workbook.Worksheets["TablesWorksheet"]
                    .Extract<NameAndAge>()
                    .WithProperty(p => p.Name, "C")
                    .WithProperty(p => p.Age, "D",
                        setPropertyCastedValueCallback: (context, age) =>
                        {
                            if (age <= 25)
                                validations.Add(string.Format("Above 25 in {0} (Row: {1}, Column: {2})",
                                    context.CellAddress.Address, context.CellAddress.Row, context.CellAddress.Column));
                        })
                    .GetData(4, 11)
                    .ToList();

                Assert.Equal(8, people.Count);

                Assert.True(people.All(person =>
                    !string.IsNullOrWhiteSpace(person.Name) && person.Age != default(int)));

                Assert.Equal(3, validations.Count);

                Assert.Contains(validations, x => x == "Above 25 in D7 (Row: 7, Column: 4)"); // Joseph, 23
                Assert.Contains(validations, x => x == "Above 25 in D9 (Row: 9, Column: 4)"); // Hudson, 24
                Assert.Contains(validations, x => x == "Above 25 in D11 (Row: 11, Column: 4)"); // Wanessa, 25
            }
        }

        [Fact]
        public void ExtractSimpleData_AbortExecutionBasedOnTheValueCasted()
        {
            var fileInfo = GetSpreadsheetFileInfo();
            using (var package = new ExcelPackage(fileInfo))
            {
                // Read the data but aborts when the "CarName" is "i30".
                // No row after the one being read will be returned, and in the current row,
                // no other columns after the "CarName" should be populated.
                var cars = package.Workbook.Worksheets["MainWorksheet"]
                    .Extract<SimpleRowData>()
                    .WithProperty(p => p.Value, "C")
                    .WithProperty(p => p.CarName, "B",
                        setPropertyValueCallback: (context, obj) =>
                        {
                            string strValue = obj as string;
                            if (strValue != null && strValue == "i30")
                                context.Abort();
                        })
                    .WithProperty(p => p.CreationDate, "D")
                    .GetData(4, 6)
                    .ToList();

                Assert.Equal(2, cars.Count);

                Assert.Contains(cars, i =>
                    i.Value == 20000 && i.CarName == "Pegeut 203" && i.CreationDate == new DateTime(2017, 07, 01));

                Assert.Contains(cars, i =>
                    i.Value == 21000 && i.CarName == default(string) && i.CreationDate == default(DateTime));
            }
        }

        [Fact]
        public void ExtractDataTransformingColumnsIntoRows()
        {
            var fileInfo = GetSpreadsheetFileInfo();
            using (var package = new ExcelPackage(fileInfo))
            {
                var data = package.Workbook.Worksheets["MainWorksheet"]
                    .Extract<RowDataWithColumnBeingRow>()
                    .WithProperty(p => p.Name, "F")
                    .WithProperty(p => p.Age, "G")
                    .WithCollectionProperty(p => p.MoneyData,
                        item => item.Date, 1,
                        item => item.ReceivedMoney, "H", "S")
                    .GetData(2, 4)
                    .ToList();

                Assert.Equal(3, data.Count);

                Assert.True(data.All(i => i.MoneyData.Count == 12));

                Assert.Contains(data,  i =>
                    i.Name == "John" && i.Age == 32 && i.MoneyData[0].Date == new DateTime(2016, 01, 01) &&
                    i.MoneyData[0].ReceivedMoney == 10);

                Assert.Contains(data,  i =>
                    i.Name == "Luis" && i.Age == 56 && i.MoneyData[6].Date == new DateTime(2016, 07, 01) &&
                    i.MoneyData[6].ReceivedMoney == 17560);

                Assert.Contains(data,  i =>
                    i.Name == "Mary" && i.Age == 16 && i.MoneyData[0].Date == new DateTime(2016, 01, 01) &&
                    i.MoneyData[0].ReceivedMoney == 12);
            }
        }

        [Fact]
        public void ExtractDataTransformingColumnsIntoRowsWithFunction()
        {
            var fileInfo = GetSpreadsheetFileInfo();
            using (var package = new ExcelPackage(fileInfo))
            {
                var data = package.Workbook.Worksheets["MainWorksheet"]
                    .Extract<RowDataWithColumnBeingRowAndWithFunction>()
                    .WithProperty(p => p.Name, "F")
                    .WithProperty(p => p.Age, "G")
                    .WithProperty(p => p.Is18OrOlder, "G",
                        (value) =>
                        {
                            int intValue;
                            if (!int.TryParse(value.ToString(), out intValue))
                                throw new InvalidCastException(string.Format("Cannot convert type {0} to int.",
                                    value.GetType()));

                            return intValue >= 18;
                        })
                    .WithCollectionProperty(
                        p => p.MoneyData,
                        item => item.Date, 1,
                        item => item.ReceivedMoney, "H", "S")
                    .GetData(2, 4)
                    .ToList();

                Assert.Equal(3, data.Count);

                Assert.True(data.All(i => i.MoneyData.Count == 12));

                Assert.Contains(data,  i =>
                    i.Name == "John" && i.Age == 32 && i.Is18OrOlder == true &&
                    i.MoneyData[0].Date == new DateTime(2016, 01, 01) && i.MoneyData[0].ReceivedMoney == 10);

                Assert.Contains(data,  i =>
                    i.Name == "Luis" && i.Age == 56 && i.Is18OrOlder == true &&
                    i.MoneyData[6].Date == new DateTime(2016, 07, 01) && i.MoneyData[6].ReceivedMoney == 17560);

                Assert.Contains(data,  i =>
                    i.Name == "Mary" && i.Age == 16 && i.Is18OrOlder == false &&
                    i.MoneyData[0].Date == new DateTime(2016, 01, 01) && i.MoneyData[0].ReceivedMoney == 12);
            }
        }

        [Fact]
        public void ExtractDataWithPredicate()
        {
            var fileInfo = GetSpreadsheetFileInfo();
            using (var package = new ExcelPackage(fileInfo))
            {
                var worksheet = package.Workbook.Worksheets["TablesWorksheet"];

                var items = worksheet
                    .Extract<ItemData>()
                    .WithProperty(p => p.MoneySpent, "G")
                    .WithProperty(p => p.Date, "H")
                    .WithProperty(p => p.MoneyReceived, "I")
                    // Read from row 2 while column 7 (G) has a value
                    .GetData(2, (row) => worksheet.Cells[row, 7].Value != null)
                    .ToList();

                // 12 rows should be read, from 2 to 13, both included.
                Assert.Equal(12, items.Count);

                // All items should have a value populated for each property that is not the default value
                Assert.True(items.All(i =>
                    i.Date != default(DateTime) && i.MoneyReceived != default(decimal) &&
                    i.MoneySpent != default(decimal)));
            }
        }
    }
}