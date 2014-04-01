/*
Copyright (c) 2014 Darren Horrocks

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/
using System;

namespace DScript
{
	public class MathFunctionProvider : IFunctionProvider
	{
		public void RegisterFunctions(ScriptEngine engine)
		{
			if(engine == null) return;

			String[] ns = {"Math"};

			engine.AddObject(ns, "PI", Pi());
			engine.AddObject(ns, "E", E());
			engine.AddMethod(ns, "Abs", new[] { "a" }, Abs, null);
			engine.AddMethod(ns, "Round", new[] { "a", "b" }, Round, null);
			engine.AddMethod(ns, "Ceil", new[] { "a" }, Ceil, null);
			engine.AddMethod(ns, "Floor", new[] { "a" }, Floor, null);
			engine.AddMethod(ns, "Min", new[] { "a", "b" }, Min, null);
			engine.AddMethod(ns, "Max", new[] { "a", "b" }, Max, null);
			engine.AddMethod(ns, "Range", new[] { "x", "a", "b" }, Range, null);
			engine.AddMethod(ns, "Sign", new[] { "a" }, Sign, null);
			engine.AddMethod(ns, "Sin", new[] { "a" }, Sin, null);
			engine.AddMethod(ns, "Cos", new[] { "a" }, Cos, null);
			engine.AddMethod(ns, "Tan", new[] { "a" }, Tan, null);
			engine.AddMethod(ns, "Sinh", new[] { "a" }, Sinh, null);
			engine.AddMethod(ns, "Cosh", new[] { "a" }, Cosh, null);
			engine.AddMethod(ns, "Tanh", new[] { "a" }, Tanh, null);
			engine.AddMethod(ns, "Asin", new[] { "a" }, ASin, null);
			engine.AddMethod(ns, "Acos", new[] { "a" }, ACos, null);
			engine.AddMethod(ns, "Atan", new[] { "a" }, ATan, null);
			engine.AddMethod(ns, "Asinh", new[] { "a" }, ASinh, null);
			engine.AddMethod(ns, "Acosh", new[] { "a" }, ACosh, null);
			engine.AddMethod(ns, "Atanh", new[] { "a" }, ATan, null);
			engine.AddMethod(ns, "Atan2", new[] { "a", "b" }, ATan2, null);
			engine.AddMethod(ns, "Pow", new[] { "a", "b" }, Pow, null);
			engine.AddMethod(ns, "Sqrt", new[] { "a" }, Sqrt, null);
			engine.AddMethod(ns, "Log", new[] { "a" }, Log, null);
			engine.AddMethod(ns, "Log10", new[] { "a" }, Log10, null);
			engine.AddMethod(ns, "Exp", new[] { "a" }, Exp, null);
		}

		private static ScriptVar Pi()
		{
			return (new ScriptVar(Math.PI));
		}

		private static ScriptVar E()
		{
			return (new ScriptVar(Math.E));
		}

		private static void Abs(ScriptVar var, object userdata)
		{
			double a = var.GetParameter("a").GetDouble();

			var.SetReturnVar(new ScriptVar(Math.Abs(a)));
		}

		private static void Round(ScriptVar var, object userdata)
		{
			double a = var.GetParameter("a").GetDouble();
			Int32 b = var.GetParameter("b").GetInt();

			var.SetReturnVar(new ScriptVar(Math.Round(a, b)));
		}

		private static void Ceil(ScriptVar var, object userdata)
		{
			double a = var.GetParameter("a").GetDouble();

			var.SetReturnVar(new ScriptVar(Math.Ceiling(a)));
		}

		private static void Floor(ScriptVar var, object userdata)
		{
			double a = var.GetParameter("a").GetDouble();

			var.SetReturnVar(new ScriptVar(Math.Floor(a)));
		}

		private static void Min(ScriptVar var, object userdata)
		{
			double a = var.GetParameter("a").GetDouble();
			double b = var.GetParameter("b").GetDouble();

			var.SetReturnVar(new ScriptVar(Math.Min(a, b)));
		}

		private static void Max(ScriptVar var, object userdata)
		{
			double a = var.GetParameter("a").GetDouble();
			double b = var.GetParameter("b").GetDouble();

			var.SetReturnVar(new ScriptVar(Math.Max(a, b)));
		}

		private static void Range(ScriptVar var, object userdata)
		{
			double x = var.GetParameter("x").GetDouble();
			double a = var.GetParameter("a").GetDouble();
			double b = var.GetParameter("b").GetDouble();

			var.SetReturnVar(new ScriptVar((x < a ? a : (x > b ? b : a ))));
		}

		private static void Sign(ScriptVar var, object userdata)
		{
			double a = var.GetParameter("a").GetDouble();

			var.SetReturnVar(new ScriptVar(Math.Sign(a)));
		}

		private static void Sin(ScriptVar var, object userdata)
		{
			double a = var.GetParameter("a").GetDouble();

			var.SetReturnVar(new ScriptVar(Math.Sin(a)));
		}

		private static void Cos(ScriptVar var, object userdata)
		{
			double a = var.GetParameter("a").GetDouble();

			var.SetReturnVar(new ScriptVar(Math.Cos(a)));
		}

		private static void Tan(ScriptVar var, object userdata)
		{
			double a = var.GetParameter("a").GetDouble();

			var.SetReturnVar(new ScriptVar(Math.Tan(a)));
		}

		private static void Sinh(ScriptVar var, object userdata)
		{
			double a = var.GetParameter("a").GetDouble();

			var.SetReturnVar(new ScriptVar(Math.Sinh(a)));
		}

		private static void Cosh(ScriptVar var, object userdata)
		{
			double a = var.GetParameter("a").GetDouble();

			var.SetReturnVar(new ScriptVar(Math.Cosh(a)));
		}

		private static void Tanh(ScriptVar var, object userdata)
		{
			double a = var.GetParameter("a").GetDouble();

			var.SetReturnVar(new ScriptVar(Math.Tanh(a)));
		}

		private static void ASin(ScriptVar var, object userdata)
		{
			double a = var.GetParameter("a").GetDouble();

			var.SetReturnVar(new ScriptVar(Math.Asin(a)));
		}

		private static void ACos(ScriptVar var, object userdata)
		{
			double a = var.GetParameter("a").GetDouble();

			var.SetReturnVar(new ScriptVar(Math.Acos(a)));
		}

		private static void ATan(ScriptVar var, object userdata)
		{
			double a = var.GetParameter("a").GetDouble();

			var.SetReturnVar(new ScriptVar(Math.Atan(a)));
		}

		private static void ASinh(ScriptVar var, object userdata)
		{
			double a = var.GetParameter("a").GetDouble();
			double returnVal;

			if (a > 0)
			{
				returnVal = Math.Log(a + Math.Sqrt(a*a + 1));
			}
			else
			{
				returnVal = -Math.Log(-a + Math.Sqrt(a * a + 1));
			}

			var.SetReturnVar(new ScriptVar(returnVal));
		}

		private static void ACosh(ScriptVar var, object userdata)
		{
			double a = var.GetParameter("a").GetDouble();
			double returnVal;

			if (a > 0)
			{
				returnVal = Math.Log(a + Math.Sqrt(a * a - 1));
			}
			else
			{
				returnVal = -Math.Log(-a + Math.Sqrt(a * a - 1));
			}

			var.SetReturnVar(new ScriptVar(returnVal));
		}

		private static void ATan2(ScriptVar var, object userdata)
		{
			double a = var.GetParameter("a").GetDouble();
			double b = var.GetParameter("b").GetDouble();

			var.SetReturnVar(new ScriptVar(Math.Atan2(a, b)));
		}

		private static void Pow(ScriptVar var, object userdata)
		{
			double a = var.GetParameter("a").GetDouble();
			double b = var.GetParameter("b").GetDouble();

			var.SetReturnVar(new ScriptVar(Math.Pow(a, b)));
		}

		private static void Sqrt(ScriptVar var, object userdata)
		{
			double a = var.GetParameter("a").GetDouble();

			var.SetReturnVar(new ScriptVar(Math.Sqrt(a)));
		}

		private static void Log(ScriptVar var, object userdata)
		{
			double a = var.GetParameter("a").GetDouble();

			var.SetReturnVar(new ScriptVar(Math.Log(a)));
		}

		private static void Log10(ScriptVar var, object userdata)
		{
			double a = var.GetParameter("a").GetDouble();

			var.SetReturnVar(new ScriptVar(Math.Log10(a)));
		}

		private static void Exp(ScriptVar var, object userdata)
		{
			double a = var.GetParameter("a").GetDouble();

			var.SetReturnVar(new ScriptVar(Math.Exp(a)));
		}
	}
}
