using NUnit.Framework;
using BlazorDatasheet.Core.Data;
using FluentAssertions;

namespace BlazorDatasheet.Test.Commands;

public class InsertRowAtCommandTests
{
    [Test]
    public void Insert_Row_Then_Undo_Correct()
    {
        var sheet = new Sheet(3, 1);
        sheet.Cells.SetValue(0, 0, "'0,0");
        sheet.Cells.SetValue(2, 0, "'2,0");
        sheet.Rows.InsertAt(0);

        Assert.AreEqual(4, sheet.NumRows);
        Assert.AreEqual("'0,0", sheet.Cells.GetValue(1, 0));
        Assert.AreEqual("'2,0", sheet.Cells.GetValue(3, 0));

        sheet.Commands.Undo();
        Assert.AreEqual(3, sheet.NumRows);
        Assert.AreEqual("'0,0", sheet.Cells.GetValue(0, 0));
        Assert.AreEqual("'2,0", sheet.Cells.GetValue(2, 0));
    }

    [Test]
    public void Insert_Row_After_End_Of_Sheet_Appends_Row_At_End()
    {
        var sheet = new Sheet(3, 1);
        sheet.Cells[2, 0].Value = "EndOfSheet";
        sheet.Cells[sheet.NumRows - 1, 0].Value.Should().Be("EndOfSheet");
        sheet.Rows.InsertAt(3);
        sheet.NumRows.Should().Be(4);
        sheet.Rows.InsertAt(10);
        // check that since we have appended after the end of the sheet,
        // there should be a new blank row
        sheet.Cells[sheet.NumRows - 1, 0].Value.Should().BeNull();
        sheet.NumRows.Should().Be(5);
        sheet.Commands.Undo();
        sheet.Commands.Undo();
        sheet.NumRows.Should().Be(3);
    }
}