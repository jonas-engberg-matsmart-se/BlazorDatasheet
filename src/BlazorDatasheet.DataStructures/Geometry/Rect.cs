﻿namespace BlazorDatasheet.DataStructures.Geometry;

public class Rect
{
    public double X { get; }
    public double Height { get; }
    public double Y { get; }
    public double Width { get; }

    public double Right => X + Width;
    public double Bottom => Y + Height;

    public Rect(double x, double y, double width, double height)
    {
        X = x;
        Height = height;
        Y = y;
        Width = width;
    }

    public override string ToString()
    {
        return $"X: {X}, Y: {Y}, Width: {Width}, Height: {Height}";
    }
}