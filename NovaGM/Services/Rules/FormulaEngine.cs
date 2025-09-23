using System;
using System.Collections.Generic;
using System.Globalization;

namespace NovaGM.Services.Rules
{
    /// Minimal expression evaluator: + - * /, parentheses, variables,
    /// functions: floor, ceil, min, max, clamp(a,b,c), mod(x)=floor((x-10)/2)
    public sealed class EvalContext
    {
        private readonly Dictionary<string, double> _vars = new(StringComparer.OrdinalIgnoreCase);
        public EvalContext With(string name, double value) { _vars[name] = value; return this; }
        public bool TryGet(string name, out double v) => _vars.TryGetValue(name, out v);
    }

    public static class FormulaEngine
    {
        public static double Eval(string expr, EvalContext ctx, IReadOnlyDictionary<string, double>? constants = null)
        {
            if (string.IsNullOrWhiteSpace(expr)) return 0;
            var p = new Parser(expr.AsSpan(), ctx, constants);
            var val = p.ParseExpr();
            p.SkipWs();
            return val;
        }

        public static int EvalInt(string expr, EvalContext ctx, IReadOnlyDictionary<string, double>? constants = null)
            => (int)Math.Floor(Eval(expr, ctx, constants) + 1e-9);

        private ref struct Parser
        {
            private ReadOnlySpan<char> _s;
            private int _i;
            private readonly EvalContext _ctx;
            private readonly IReadOnlyDictionary<string, double>? _consts;

            public Parser(ReadOnlySpan<char> s, EvalContext ctx, IReadOnlyDictionary<string, double>? c) { _s = s; _i = 0; _ctx = ctx; _consts = c; }

            public double ParseExpr()
            {
                var v = ParseTerm();
                while (true)
                {
                    SkipWs();
                    if (Match('+')) v += ParseTerm();
                    else if (Match('-')) v -= ParseTerm();
                    else break;
                }
                return v;
            }

            private double ParseTerm()
            {
                var v = ParseFactor();
                while (true)
                {
                    SkipWs();
                    if (Match('*')) v *= ParseFactor();
                    else if (Match('/')) v /= ParseFactor();
                    else break;
                }
                return v;
            }

            private double ParseFactor()
            {
                SkipWs();
                if (Match('('))
                {
                    var v = ParseExpr();
                    Expect(')');
                    return v;
                }
                if (PeekIsLetter())
                {
                    var name = ParseIdent();
                    SkipWs();
                    if (Match('('))
                    {
                        // function call
                        var args = new List<double>();
                        SkipWs();
                        if (!Peek(')'))
                        {
                            while (true)
                            {
                                args.Add(ParseExpr());
                                SkipWs();
                                if (Match(',')) continue;
                                break;
                            }
                        }
                        Expect(')');
                        return CallFunc(name, args);
                    }
                    // variable or constant
                    if (_ctx.TryGet(name, out var v1)) return v1;
                    if (_consts != null && _consts.TryGetValue(name, out var c)) return c;
                    return 0;
                }
                if (Match('+')) return ParseFactor();
                if (Match('-')) return -ParseFactor();
                return ParseNumber();
            }

            private double CallFunc(string name, List<double> args)
            {
                switch (name.ToLowerInvariant())
                {
                    case "floor": return Math.Floor(args[0]);
                    case "ceil":  return Math.Ceiling(args[0]);
                    case "min":   return Math.Min(args[0], args[1]);
                    case "max":   return Math.Max(args[0], args[1]);
                    case "clamp": return Math.Max(args[1], Math.Min(args[2], args[0]));
                    case "mod":   return Math.Floor((args[0] - 10.0) / 2.0); // D20-style ability mod
                    default:      return 0;
                }
            }

            private double ParseNumber()
            {
                SkipWs();
                int start = _i;
                while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.')) _i++;
                if (start == _i) return 0;
                var span = _s.Slice(start, _i - start);
                if (double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return v;
                return 0;
            }

            private string ParseIdent()
            {
                int start = _i;
                while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '_' )) _i++;
                return new string(_s.Slice(start, _i - start));
            }

            public void SkipWs() { while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++; }
            private bool Peek(char c) => _i < _s.Length && _s[_i] == c;
            private bool Match(char c) { if (Peek(c)) { _i++; return true; } return false; }
            private void Expect(char c) { if (!Match(c)) throw new Exception($"Expected '{c}'"); }
            private bool PeekIsLetter() => _i < _s.Length && char.IsLetter(_s[_i]);
        }
    }
}
