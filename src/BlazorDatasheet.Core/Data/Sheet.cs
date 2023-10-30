using System.Diagnostics;
using System.Text;
using BlazorDatasheet.Core.Commands;
using BlazorDatasheet.Core.Data.Cells;
using BlazorDatasheet.Core.Edit;
using BlazorDatasheet.Core.Events;
using BlazorDatasheet.Core.Events.Formula;
using BlazorDatasheet.Core.Events.Layout;
using BlazorDatasheet.Core.Events.Visual;
using BlazorDatasheet.Core.Formats;
using BlazorDatasheet.Core.Interfaces;
using BlazorDatasheet.Core.Selecting;
using BlazorDatasheet.Core.Validation;
using BlazorDatasheet.DataStructures.Geometry;
using BlazorDatasheet.DataStructures.Intervals;
using BlazorDatasheet.DataStructures.Store;
using BlazorDatasheet.Formula.Core;
using BlazorDatasheet.Formula.Core.Interpreter.References;

namespace BlazorDatasheet.Core.Data;

public class Sheet
{
    /// <summary>
    /// The total number of rows in the sheet
    /// </summary>
    public int NumRows { get; private set; }

    /// <summary>
    /// The total of columns in the sheet
    /// </summary>
    public int NumCols { get; private set; }

    /// <summary>
    /// Start/finish edits.
    /// </summary>
    public Editor Editor { get; }

    /// <summary>
    /// Interact with cells & cell data.
    /// </summary>
    public CellStore Cells { get; }

    /// <summary>
    /// Managers commands & undo/redo. Default is true.
    /// </summary>
    public CommandManager Commands { get; }

    /// <summary>
    /// Manages sheet formula
    /// </summary>
    public FormulaEngine.FormulaEngine FormulaEngine { get; }

    /// <summary>
    /// The bounds of the sheet
    /// </summary>
    public Region Region => new Region(0, NumRows - 1, 0, NumCols - 1);

    /// <summary>
    /// Provides functions for managing the sheet's conditional formatting
    /// </summary>
    public ConditionalFormatManager ConditionalFormatting { get; }

    /// <summary>
    /// Manages and holds information on cell validators.
    /// </summary>
    internal ValidationManager Validators { get; } = new();

    /// <summary>
    /// Contains data, including width, on each column.
    /// </summary>
    public ColumnInfoStore Columns { get; private set; }

    /// <summary>
    /// Contains data, including height, on each row.
    /// </summary>
    public RowInfoStore Rows { get; private set; }

    /// <summary>
    /// The sheet's active selection
    /// </summary>
    public Selection Selection { get; }

    internal IDialogService Dialog { get; private set; }

    #region EVENTS

    /// <summary>
    /// Fired when a row is inserted into the sheet
    /// </summary>
    public event EventHandler<RowInsertedEventArgs>? RowInserted;

    /// <summary>
    /// Fired when a row is removed from the sheet.
    /// </summary>
    public event EventHandler<RowRemovedEventArgs>? RowRemoved;

    /// <summary>
    /// Fired when a column is inserted into the sheet
    /// </summary>
    public event EventHandler<ColumnInsertedEventArgs>? ColumnInserted;

    /// <summary>
    /// Fired when a column is removed from the sheet.
    /// </summary>
    public event EventHandler<ColumnRemovedEventArgs>? ColumnRemoved;

    /// <summary>
    /// Fired when one or more cells are changed
    /// </summary>
    public event EventHandler<IEnumerable<(int row, int col)>>? CellsChanged;

    /// <summary>
    /// Fired when a column width is changed
    /// </summary>
    public event EventHandler<ColumnWidthChangedEventArgs>? ColumnWidthChanged;

    /// <summary>
    /// Fired when a row height is changed.
    /// </summary>
    public event EventHandler<RowHeightChangedEventArgs>? RowHeightChanged;

    /// <summary>
    /// Fired when a portion of the sheet is marked as dirty.
    /// </summary>
    public event EventHandler<DirtySheetEventArgs>? SheetDirty;

    public event EventHandler<CellsSelectedEventArgs>? CellsSelected;

    public event EventHandler<CellMetaDataChangeEventArgs>? MetaDataChanged;

    public event EventHandler<CellFormulaChangeEventArgs>? FormulaChanged;

    /// <summary>
    /// Fired when cell formats change
    /// </summary>
    public event EventHandler<FormatChangedEventArgs>? FormatsChanged;

    /// <summary>
    /// Fired when the sheet is invalidated (requires re-render).
    /// </summary>
    public event EventHandler<SheetInvalidateEventArgs>? SheetInvalidated;

    #endregion

    /// <summary>
    /// True if the sheet is not firing dirty events until <see cref="EndBatchUpdates"/> is called.
    /// </summary>
    private bool _isBatchingChanges;


    /// <summary>
    /// If the sheet is batching dirty regions, they are stored here.
    /// </summary>
    private List<IRegion> _dirtyRegions = new();

    /// <summary>
    /// If the sheet is batching changes, they are stored here.
    /// </summary>
    private HashSet<(int row, int col)> _cellsChanged = new();

    /// <summary>
    /// If the sheet is batching dirty cells, they are stored here.
    /// </summary>
    private HashSet<(int row, int col)> _dirtyPositions = new();

    private Sheet()
    {
        Cells = new Cells.CellStore(this);
        Commands = new CommandManager(this);
        Selection = new Selection(this);
        Editor = new Editor(this);
        Rows = new RowInfoStore(25, this);
        Columns = new ColumnInfoStore(105, this);
        FormulaEngine = new FormulaEngine.FormulaEngine(this);
        ConditionalFormatting = new ConditionalFormatManager(this);
    }

    public Sheet(int numRows, int numCols) : this()
    {
        Cells = new Cells.CellStore(this);
        NumCols = numCols;
        NumRows = numRows;
    }


    #region COLS

    internal void InsertColAtImpl(int colIndex, int nCols = 1)
    {
        NumCols += nCols;
        ColumnInserted?.Invoke(this, new ColumnInsertedEventArgs(colIndex, nCols));
    }

    /// <summary>
    /// Internal implementation that reduces number of cols and invokes event.
    /// </summary>
    /// <param name="colIndex"></param>
    /// <returns>Whether the column at index colIndex was removed</returns>
    internal bool RemoveColImpl(int colIndex, int nCols = 1)
    {
        NumCols -= nCols;
        ColumnRemoved?.Invoke(this, new ColumnRemovedEventArgs(colIndex, nCols));
        return true;
    }

    internal void EmitColumnWidthChange(int colIndex, int colEnd, double width)
    {
        ColumnWidthChanged?.Invoke(this, new ColumnWidthChangedEventArgs(colIndex, colEnd, width));
    }

    #endregion

    #region ROWS

    /// <summary>
    /// Internal implementation that increases number of rows and invokes event.
    /// </summary>
    /// <param name="rowIndex"></param>
    /// <param name="nRows">The number of rows to insert</param>
    /// <param name="height">The height of each row that is inserted. Default is the default row height</param>
    /// <returns></returns>
    internal bool InsertRowAtImpl(int rowIndex, int nRows = 1)
    {
        NumRows += nRows;
        RowInserted?.Invoke(this, new RowInsertedEventArgs(rowIndex, nRows));
        return true;
    }

    internal bool RemoveRowAtImpl(int rowIndex, int nRows)
    {
        NumRows -= nRows;
        RowRemoved?.Invoke(this, new RowRemovedEventArgs(rowIndex, nRows));
        return true;
    }


    internal void EmitRowHeightChange(int rowStart, int rowEnd, double height)
    {
        RowHeightChanged?.Invoke(this, new RowHeightChangedEventArgs(rowStart, rowEnd, height));
    }

    #endregion

    /// <summary>
    /// Returns a single cell range at the position row, col
    /// </summary>
    /// <param name="row"></param>
    /// <param name="col"></param>
    /// <returns></returns>
    public BRangeCell Range(int row, int col)
    {
        return new BRangeCell(this, row, col);
    }

    /// <summary>
    /// Returns a range with the positions specified
    /// </summary>
    /// <param name="rowStart"></param>
    /// <param name="rowEnd"></param>
    /// <param name="colStart"></param>
    /// <param name="colEnd"></param>
    /// <returns></returns>
    public BRange Range(int rowStart, int rowEnd, int colStart, int colEnd)
    {
        return Range(new Region(rowStart, rowEnd, colStart, colEnd));
    }

    /// <summary>
    /// Returns a new range that contains the region specified
    /// </summary>
    /// <param name="region"></param>
    /// <returns></returns>
    public BRange Range(IRegion region)
    {
        return Range(new List<IRegion>() { region });
    }

    /// <summary>
    /// Returns a column or row range, depending on the axis provided
    /// </summary>
    /// <param name="axis">The axis of the range (row or column)</param>
    /// <param name="start">The start row/column index</param>
    /// <param name="end">The end row/column index</param>
    /// <returns></returns>
    public BRange Range(Axis axis, int start, int end)
    {
        switch (axis)
        {
            case Axis.Col:
                return Range(new ColumnRegion(start, end));
            case Axis.Row:
                return Range(new RowRegion(start, end));
        }

        throw new Exception("Cannot return a range for axis " + axis);
    }

    /// <summary>
    /// Returns a new range that contains all the regions specified
    /// </summary>
    /// <param name="regions"></param>
    /// <returns></returns>
    public BRange Range(IEnumerable<IRegion> regions)
    {
        return new BRange(this, regions);
    }


    #region VALIDATION

    /// <summary>
    /// Add a <see cref="IDataValidator"> to a region.
    /// </summary>
    /// <param name="region"></param>
    /// <param name="validator"></param>
    public void AddValidator(IRegion region, IDataValidator validator)
    {
        var cmd = new SetValidatorCommand(region, validator);
        Commands.ExecuteCommand(cmd);
    }

    /// <summary>
    /// Add a <see cref="IDataValidator"> to a cell.
    /// </summary>
    /// <param name="col"></param>
    /// <param name="validator"></param>
    /// <param name="row"></param>
    public void AddValidator(int row, int col, IDataValidator validator)
    {
        var cmd = new SetValidatorCommand(new Region(row, col), validator);
        Commands.ExecuteCommand(cmd);
    }

    /// <summary>
    /// Adds multiple validators to a region.
    /// </summary>
    /// <param name="region"></param>
    /// <param name="validators"></param>
    public void AddValidators(IRegion region, IEnumerable<IDataValidator> validators)
    {
        Commands.BeginCommandGroup();
        foreach (var validator in validators)
        {
            AddValidator(region, validator);
        }

        Commands.EndCommandGroup();
    }

    public IEnumerable<IDataValidator> GetValidators(int cellRow, int cellCol)
    {
        return Validators.Get(cellRow, cellCol);
    }

    #endregion

    #region DATA

    internal void EmitCellsChanged(IEnumerable<(int row, int col)> positions)
    {
        if (_isBatchingChanges)
        {
            foreach (var pos in positions)
                _cellsChanged.Add(pos);
        }
        else
        {
            CellsChanged?.Invoke(this, positions);
        }
    }

    internal void EmitCellChanged(int row, int col)
    {
        EmitCellsChanged(new[] { (row, col) });
    }

    #endregion

    /// <summary>
    /// Mark the cells specified by positions dirty.
    /// </summary>
    /// <param name="positions"></param>
    internal void MarkDirty(IEnumerable<(int row, int col)> positions)
    {
        if (_isBatchingChanges)
        {
            foreach (var position in positions)
                _dirtyPositions.Add(position);
        }
        else
            SheetDirty?.Invoke(this, new DirtySheetEventArgs()
            {
                DirtyPositions = positions.ToHashSet()
            });
    }

    /// <summary>
    /// Marks the cell as dirty and requiring re-render
    /// </summary>
    /// <param name="row"></param>
    /// <param name="col"></param>
    internal void MarkDirty(int row, int col)
    {
        if (_isBatchingChanges)
            _dirtyPositions.Add((row, col));
        else
            SheetDirty?.Invoke(this, new DirtySheetEventArgs()
            {
                DirtyPositions = new HashSet<(int row, int col)>() { (row, col) }
            });
    }

    /// <summary>
    /// Marks the region as dirty and requiring re-render.
    /// </summary>
    /// <param name="region"></param>
    internal void MarkDirty(IRegion region)
    {
        MarkDirty(new List<IRegion>() { region });
    }

    /// <summary>
    /// Marks the regions as dirty and requiring re-render.
    /// </summary>
    /// <param name="regions"></param>
    internal void MarkDirty(IEnumerable<IRegion> regions)
    {
        if (_isBatchingChanges)
            _dirtyRegions.AddRange(regions);
        else
            SheetDirty?.Invoke(
                this, new DirtySheetEventArgs() { DirtyRegions = regions, DirtyPositions = _dirtyPositions });
    }

    /// <summary>
    /// Batches dirty cell and region additions, as well as cell value changes to emit events once rather
    /// than every time a cell is dirty or a value is changed.
    /// </summary>
    internal void BatchUpdates()
    {
        if (!_isBatchingChanges)
        {
            _dirtyPositions.Clear();
            _dirtyRegions.Clear();
            _cellsChanged.Clear();
        }

        _isBatchingChanges = true;
    }

    /// <summary>
    /// Ends the batching of dirty cells and regions, and emits the dirty sheet event.
    /// </summary>
    internal void EndBatchUpdates()
    {
        if (_cellsChanged.Any() && _isBatchingChanges)
            CellsChanged?.Invoke(this, _cellsChanged.AsEnumerable());

        // Checks for batching changes here, because the cells changed event
        // may start batching more dirty changes again from subscribers of that event.
        if (_dirtyRegions.Any() || _dirtyPositions.Any() && _isBatchingChanges)
        {
            SheetDirty?.Invoke(this, new DirtySheetEventArgs()
            {
                DirtyRegions = _dirtyRegions,
                DirtyPositions = _dirtyPositions
            });
        }

        _isBatchingChanges = false;
    }

    /// <summary>
    /// Inserts delimited text from the given position
    /// And assigns cell's values based on the delimited text (tabs and newlines)
    /// Returns the region of cells that surrounds all cells that are affected
    /// </summary>
    /// <param name="text">The text to insert</param>
    /// <param name="inputPosition">The position where the insertion starts</param>
    /// <param name="newLineDelim">The delimiter that specifies a new-line</param>
    /// <returns>The region of cells that were affected</returns>
    public Region? InsertDelimitedText(string text, CellPosition inputPosition, string newLineDelim = "\n")
    {
        if (!Region.Contains(inputPosition))
            return null;

        if (text.EndsWith('\n'))
            text = text.Substring(0, text.Length - 1);
        var lines = text.Split(newLineDelim);

        // We may reach the end of the sheet, so we only need to paste the rows up until the end.
        var endRow = Math.Min(inputPosition.Row + lines.Length - 1, NumRows - 1);
        // Keep track of the maximum end column that we are inserting into
        // This is used to determine the region to return.
        // It is possible that each line is of different cell lengths, so we return the max for all lines
        var maxEndCol = -1;

        var valChanges = new List<(int row, int col, object value)>();

        int lineNo = 0;
        for (int row = inputPosition.Row; row <= endRow; row++)
        {
            var lineSplit = lines[lineNo].Split('\t');
            // Same thing as above with the number of columns
            var endCol = Math.Min(inputPosition.Col + lineSplit.Length - 1, NumCols - 1);

            maxEndCol = Math.Max(endCol, maxEndCol);

            int cellIndex = 0;
            for (int col = inputPosition.Col; col <= endCol; col++)
            {
                valChanges.Add((row, col, lineSplit[cellIndex]));
                cellIndex++;
            }

            lineNo++;
        }

        Cells.SetValues(valChanges);

        return new Region(inputPosition.Row, endRow, inputPosition.Col, maxEndCol);
    }

    #region FORMAT

    /// <summary>
    /// Returns the format that is visible at the cell position row, col.
    /// The order to determine which format is visible is
    /// 1. Cell format (if it exists)
    /// 2. Column format
    /// 3. Row format
    /// </summary>
    /// <param name="row"></param>
    /// <param name="col"></param>
    /// <returns></returns>
    public CellFormat? GetFormat(int row, int col)
    {
        var defaultFormat = new CellFormat();
        var cellFormat = Cells.GetFormat(row, col).Clone();
        var rowFormat = Rows.RowFormats.Get(row)?.Clone() ?? defaultFormat;
        var colFormat = Columns.ColFormats.Get(col)?.Clone() ?? defaultFormat;

        rowFormat.Merge(colFormat);
        rowFormat.Merge(cellFormat);
        return rowFormat;
    }

    /// <summary>
    /// Sets the format for a particular range
    /// </summary>
    /// <param name="cellFormat"></param>
    /// <param name="range"></param>
    public void SetFormat(CellFormat cellFormat, BRange range)
    {
        BatchUpdates();

        Commands.BeginCommandGroup();
        foreach (var region in range.Regions)
        {
            var cmd = new SetFormatCommand(region, cellFormat);
            Commands.ExecuteCommand(cmd);
        }

        Commands.EndCommandGroup();
        EndBatchUpdates();
    }

    internal void EmitFormatChanged(FormatChangedEventArgs args)
    {
        FormatsChanged?.Invoke(this, args);
    }

    #endregion

    public string? GetRegionAsDelimitedText(IRegion inputRegion, char tabDelimiter = '\t', string newLineDelim = "\n")
    {
        if (inputRegion.Area == 0)
            return string.Empty;

        var intersection = inputRegion.GetIntersection(this.Region);
        if (intersection == null)
            return null;

        var range = intersection.Copy();

        var strBuilder = new StringBuilder();

        var r0 = range.TopLeft.Row;
        var r1 = range.BottomRight.Row;
        var c0 = range.TopLeft.Col;
        var c1 = range.BottomRight.Col;

        for (int row = r0; row <= r1; row++)
        {
            for (int col = c0; col <= c1; col++)
            {
                var cell = Cells.GetCell(row, col);
                var value = cell.GetValue();
                if (value == null)
                    strBuilder.Append("");
                else
                {
                    if (value is string s)
                    {
                        strBuilder.Append(s.Replace(newLineDelim, " ").Replace(tabDelimiter, ' '));
                    }
                    else
                    {
                        strBuilder.Append(value);
                    }
                }

                if (col != c1)
                    strBuilder.Append(tabDelimiter);
            }

            strBuilder.Append(newLineDelim);
        }

        return strBuilder.ToString();
    }

    public void SetDialogService(IDialogService service)
    {
        Dialog = service;
    }
}