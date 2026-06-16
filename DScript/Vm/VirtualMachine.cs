/*
Copyright (c) 2014 - 2020 Darren Horrocks

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

using System.Collections.Generic;
using System.Globalization;

namespace DScript.Vm
{
    /// <summary>
    /// Stack-based virtual machine that executes compiled <see cref="Chunk"/>
    /// bytecode. Phase 1 implements the expression/stack/control-flow core;
    /// variable, property, function, and exception opcodes are filled in by
    /// later phases.
    /// </summary>
    public sealed partial class VirtualMachine
    {
        private readonly List<ScriptVar> stack = [];

        private void Push(ScriptVar value) => stack.Add(value);

        private ScriptVar Pop()
        {
            var top = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            return top;
        }

        private ScriptVar Peek() => stack[stack.Count - 1];

        /// <summary>
        /// Execute a top-level chunk and return the produced value (the operand
        /// of a <see cref="OpCode.Return"/>, or undefined when the chunk halts).
        /// </summary>
        public ScriptVar Run(Chunk chunk)
        {
            var startDepth = stack.Count;
            var result = Execute(chunk);

            // discard anything the chunk left behind to keep the stack balanced
            while (stack.Count > startDepth)
            {
                Pop();
            }

            return result ?? new ScriptVar(null, ScriptVar.Flags.Undefined);
        }

        private ScriptVar Execute(Chunk chunk)
        {
            var code = chunk.Code;
            var ip = 0;

            while (ip < code.Count)
            {
                var op = (OpCode)code[ip];
                ip++;

                switch (op)
                {
                    case OpCode.Constant:
                        Push(chunk.Constants[ReadOperand(chunk, ref ip)].Materialize());
                        break;
                    case OpCode.PushUndefined:
                        Push(new ScriptVar(null, ScriptVar.Flags.Undefined));
                        break;
                    case OpCode.PushNull:
                        Push(new ScriptVar(null, ScriptVar.Flags.Null));
                        break;
                    case OpCode.PushTrue:
                        Push(new ScriptVar(1));
                        break;
                    case OpCode.PushFalse:
                        Push(new ScriptVar(0));
                        break;

                    case OpCode.Pop:
                        Pop();
                        break;
                    case OpCode.Dup:
                        Push(Peek());
                        break;

                    case OpCode.Binary:
                    {
                        var operatorCode = (ScriptLex.LexTypes)ReadOperand(chunk, ref ip);
                        var b = Pop();
                        var a = Pop();
                        Push(a.MathsOp(b, operatorCode));
                        break;
                    }
                    case OpCode.Shift:
                    {
                        var operatorCode = (ScriptLex.LexTypes)ReadOperand(chunk, ref ip);
                        var b = Pop();
                        var a = Pop();
                        Push(ApplyShift(a, b, operatorCode));
                        break;
                    }
                    case OpCode.Negate:
                    {
                        var a = Pop();
                        Push(new ScriptVar(0).MathsOp(a, (ScriptLex.LexTypes)'-'));
                        break;
                    }
                    case OpCode.Not:
                    {
                        var a = Pop();
                        Push(a.MathsOp(new ScriptVar(0), ScriptLex.LexTypes.Equal));
                        break;
                    }
                    case OpCode.BitNot:
                    {
                        var a = Pop();
                        Push(new ScriptVar(~a.Int));
                        break;
                    }
                    case OpCode.ToNumber:
                    {
                        var a = Pop();
                        Push(CoerceToNumber(a));
                        break;
                    }

                    case OpCode.Jump:
                        ip = ReadOperand(chunk, ref ip);
                        break;
                    case OpCode.JumpIfFalse:
                    {
                        var target = ReadOperand(chunk, ref ip);
                        if (!Pop().Bool) ip = target;
                        break;
                    }
                    case OpCode.JumpIfTrue:
                    {
                        var target = ReadOperand(chunk, ref ip);
                        if (Pop().Bool) ip = target;
                        break;
                    }
                    case OpCode.JumpIfFalseOrPop:
                    {
                        var target = ReadOperand(chunk, ref ip);
                        if (!Peek().Bool) ip = target; else Pop();
                        break;
                    }
                    case OpCode.JumpIfTrueOrPop:
                    {
                        var target = ReadOperand(chunk, ref ip);
                        if (Peek().Bool) ip = target; else Pop();
                        break;
                    }

                    case OpCode.Return:
                        return Pop();
                    case OpCode.Halt:
                        return null;

                    default:
                        throw new ScriptException($"VM opcode not yet implemented: {op}");
                }
            }

            return null;
        }

        private static int ReadOperand(Chunk chunk, ref int ip)
        {
            var value = chunk.ReadInt(ip);
            ip += 4;
            return value;
        }

        private static ScriptVar ApplyShift(ScriptVar a, ScriptVar b, ScriptLex.LexTypes op)
        {
            var shift = b.Int;
            switch (op)
            {
                case ScriptLex.LexTypes.LShift: return new ScriptVar(a.Int << shift);
                case ScriptLex.LexTypes.RShift: return new ScriptVar(a.Int >> shift);
                case ScriptLex.LexTypes.RShiftUnsigned: return new ScriptVar(a.Int >>> shift);
                default: throw new ScriptException("Unsupported shift operator");
            }
        }

        private static ScriptVar CoerceToNumber(ScriptVar value)
        {
            if (value.IsInt) return new ScriptVar(value.Int);
            if (value.IsDouble) return new ScriptVar(value.Float);
            if (value.IsNull) return new ScriptVar(0);

            if (value.IsString &&
                double.TryParse(value.String, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            {
                return new ScriptVar(parsed);
            }

            return new ScriptVar(double.NaN);
        }
    }
}
