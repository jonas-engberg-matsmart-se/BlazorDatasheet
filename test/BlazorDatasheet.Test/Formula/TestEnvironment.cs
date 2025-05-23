﻿using System;
using System.Collections.Generic;
using System.Linq;
using BlazorDatasheet.DataStructures.Geometry;
using BlazorDatasheet.DataStructures.Util;
using BlazorDatasheet.Formula.Core;
using BlazorDatasheet.Formula.Core.Interpreter;
using BlazorDatasheet.Formula.Core.Interpreter.References;

namespace BlazorDatasheet.Test.Formula;

public class TestEnvironment : IEnvironment
{
    private Dictionary<CellPosition, CellValue> _cellValues = new();
    private Dictionary<string, ISheetFunction> _functions = new();
    private Dictionary<string, CellValue> _variables = new();
    private Dictionary<CellPosition, CellFormula> _formulas = new();

    public void SetCellValue(int row, int col, object val)
    {
        SetCellValue(new CellPosition(row, col), val);
    }

    public void SetCellValue(CellPosition position, object val)
    {
        _cellValues.TryAdd(position, new CellValue(val));
        _cellValues[position] = new CellValue(val);
    }

    public void SetCellFormula(int row, int col, CellFormula formula)
    {
        var position = new CellPosition(row, col);
        if (_formulas.TryAdd(position, formula))
            _formulas[position] = formula;
    }

    public void RegisterFunction(string name, ISheetFunction functionDefinition)
    {
        var validator = new FunctionParameterValidator();
        validator.ValidateOrThrow(functionDefinition.GetParameterDefinitions());

        if (!_functions.ContainsKey(name.ToLower()))
            _functions.Add(name.ToLower(), functionDefinition);
        _functions[name.ToLower()] = functionDefinition;
    }

    public IEnumerable<FunctionDefinition> SearchForFunctions(string functionName)
    {
        var funcLower = functionName.ToLower();
        return _functions.Where(x => x.Key.StartsWith(funcLower))
            .Select(x => new FunctionDefinition(x.Key, x.Value));
    }

    public IEnumerable<CellValue> GetNonEmptyInRange(Reference reference)
    {
        return GetRangeValues(reference)
            .SelectMany(x => x);
    }

    public void SetCellValue(int row, int col, string sheetName, CellValue value)
    {
        _cellValues.TryAdd(new CellPosition(row, col), value);
    }

    public void ClearVariable(string varName)
    {
        _variables.Remove(varName);
    }

    public IEnumerable<string> GetVariableNames()
    {
        return _variables.Keys;
    }

    public void SetVariable(string name, object variable)
    {
        SetVariable(name, new CellValue(variable));
    }

    public void SetVariable(string name, CellValue value)
    {
        if (!_variables.ContainsKey(name))
            _variables.Add(name, value);
        _variables[name] = value;
    }

    public CellValue GetCellValue(int row, int col, string sheetName)
    {
        var hasVal = _cellValues.TryGetValue(new CellPosition(row, col), out var val);
        if (hasVal)
            return val;
        return CellValue.Empty;
    }

    public CellFormula? GetFormula(int row, int col, string sheetName)
    {
        var hasVal = _formulas.TryGetValue(new CellPosition(row, col), out var val);
        if (hasVal)
            return val;
        return null;
    }

    public CellValue[][] GetRangeValues(Reference reference)
    {
        if (reference.Kind == ReferenceKind.Range)
        {
            var rangeRef = (RangeReference)reference;
            var r = reference.Region;
            return GetValuesInRange(r.Top, r.Bottom, r.Left, r.Right);
        }

        if (reference.Kind == ReferenceKind.Cell)
        {
            var cellRef = (CellReference)reference;
            return new[] { new[] { GetCellValue(cellRef.RowIndex, cellRef.ColIndex, cellRef.SheetName) } };
        }

        return Array.Empty<CellValue[]>();
    }

    private CellValue[][] GetValuesInRange(int r0, int r1, int c0, int c1)
    {
        r0 = Math.Clamp(r0, 0, RangeText.MaxRows);
        r1 = Math.Clamp(r1, 0, RangeText.MaxRows);

        c0 = Math.Clamp(c0, 0, RangeText.MaxCols);
        c1 = Math.Clamp(c1, 0, RangeText.MaxCols);

        var h = (r1 - r0) + 1;
        var w = (c1 - c0) + 1;
        var arr = new CellValue[h][];

        for (int i = 0; i < h; i++)
        {
            arr[i] = new CellValue[w];
            for (int j = 0; j < w; j++)
            {
                arr[i][j] = GetCellValue(r0 + i, c0 + j, "Sheet1");
            }
        }

        return arr;
    }

    public bool FunctionExists(string functionIdentifier)
    {
        return _functions.ContainsKey(functionIdentifier.ToLower());
    }

    public ISheetFunction? GetFunctionDefinition(string identifierText)
    {
        return _functions.GetValueOrDefault(identifierText.ToLower());
    }

    public bool VariableExists(string variableIdentifier)
    {
        return _variables.ContainsKey(variableIdentifier);
    }

    public CellValue GetVariable(string variableIdentifier)
    {
        return _variables[variableIdentifier];
    }
}