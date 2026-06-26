using DScript;
using DScript.Extras;
using DScript.Extras.FunctionProviders;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace DScript.Test
{
    /// <summary>
    /// Correctness checks for the benchmark suite in bench.ds.
    /// Timing is irrelevant; each test verifies the return value of one benchmark function.
    /// The output format is: "&lt;Name&gt;: &lt;time&gt;ms &lt;result&gt;".
    /// The suite runs once (OneTimeSetUp) and results are shared across all tests.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class BenchmarkTests
    {
        private static List<string> s_lines = new();
        private static List<string> s_errors = new();

        private const string BenchScript =
            @"const benchmark=(n,f)=>{const s=performance.now();const r=f();console.log(`${n}: ${(performance.now()-s).toFixed(2)}ms`,r)};benchmark(""Arrays"",()=>Array.from({length:1e6},(_,i)=>i).filter(x=>x&1).map(x=>x*3).reduce((a,b)=>a+b,0));benchmark(""Objects"",()=>{let s=0;for(let i=0;i<1e6;i++){const o={a:i,b:i+1,c:i+2,d:i+3};s+=o.a+o.b+o.c+o.d}return s});benchmark(""JSON"",()=>JSON.parse(JSON.stringify(Array.from({length:5e4},(_,i)=>({id:i,name:`User${i}`,active:i&1})))).length);benchmark(""Map"",()=>{const m=new Map();for(let i=0;i<5e5;i++)m.set(i,i);let s=0;for(let i=0;i<5e5;i++)s+=m.get(i);return s});benchmark(""Set"",()=>{const s=new Set();for(let i=0;i<5e5;i++)s.add(i);return s.has(123456)});benchmark(""Regex"",()=>{let t="""";for(let i=0;i<5e4;i++)t+=`user${i}@test.com `;let s=0;for(const m of t.matchAll(/user(\d+)@test\.com/g))s+=+m[1];return s});benchmark(""Strings"",()=>{let s="""";for(let i=0;i<1e5;i++)s+=i.toString(36);return s.length});benchmark(""Closures"",()=>{let s=0;for(let i=0;i<1e5;i++){const f=x=>()=>x; s+=f(i)()}return s});benchmark(""Functions"",()=>{function f(a,b,c){return a+b+c}let s=0;for(let i=0;i<1e7;i++)s+=f(i,1,2);return s});benchmark(""Classes"",()=>{class P{constructor(x,y){this.x=x;this.y=y}sum(){return this.x+this.y}}let s=0;for(let i=0;i<5e5;i++)s+=new P(i,i+1).sum();return s});benchmark(""TypedArrays"",()=>{const a=new Float64Array(1e6);for(let i=0;i<a.length;i++)a[i]=i;let s=0;for(let i=0;i<a.length;i++)s+=a[i];return s});benchmark(""BigInt"",()=>{let x=1n;for(let i=0;i<1e5;i++)x=x*3n+1n;return x.toString().length});benchmark(""Sort"",()=>Array.from({length:2e5},()=>Math.random()).sort((a,b)=>a-b)[0]);benchmark(""Date"",()=>{let s=0;for(let i=0;i<1e5;i++)s+=new Date(2025,0,(i%28)+1).getTime();return s});benchmark(""Spread"",()=>{let o={a:1,b:2,c:3};for(let i=0;i<1e5;i++)o={...o,d:i};return o.d});";

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            s_lines  = new List<string>();
            s_errors = new List<string>();
            ConsoleFunctionProvider.SetOutput(
                stdout: line => s_lines.Add(line),
                stderr: line => s_errors.Add(line));
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            engine.Execute(BenchScript);
            ScriptEngine.DrainMicroTasks();
            ConsoleFunctionProvider.ResetOutput();
        }

        /// <summary>
        /// Extracts the result token from a line like "Arrays: 2173.55ms 750000000000".
        /// </summary>
        private static string ResultOf(string benchmarkName)
        {
            var prefix = benchmarkName + ": ";
            var line   = s_lines.FirstOrDefault(l => l.StartsWith(prefix));
            if (line == null) return $"<missing:{benchmarkName}>";
            var msIdx = line.IndexOf("ms ", System.StringComparison.Ordinal);
            return msIdx >= 0 ? line[(msIdx + 3)..] : $"<no-result:{line}>";
        }

        // ── individual result checks ──────────────────────────────────────────

        [Test]
        public void Arrays_()
            => Assert.That(ResultOf("Arrays"), Is.EqualTo("750000000000"));

        [Test]
        public void Objects_()
            => Assert.That(ResultOf("Objects"), Is.EqualTo("2000004000000"));

        [Test]
        public void Json_()
            => Assert.That(ResultOf("JSON"), Is.EqualTo("50000"));

        [Test]
        public void Map_()
            => Assert.That(ResultOf("Map"), Is.EqualTo("124999750000"));

        [Test]
        public void Set_()
            // s.has(123456) returns boolean true; DScript represents booleans as integers (1/0)
            => Assert.That(ResultOf("Set"), Is.EqualTo("1"));

        [Test]
        public void Regex_()
            => Assert.That(ResultOf("Regex"), Is.EqualTo("1249975000"));

        [Test]
        public void Strings_()
            => Assert.That(ResultOf("Strings"), Is.EqualTo("352012"));

        [Test]
        public void Closures_()
            => Assert.That(ResultOf("Closures"), Is.EqualTo("4999950000"));

        [Test]
        public void Functions_()
            => Assert.That(ResultOf("Functions"), Is.EqualTo("50000025000000"));

        [Test]
        public void Classes_()
            => Assert.That(ResultOf("Classes"), Is.EqualTo("250000000000"));

        [Test]
        public void TypedArrays_()
            => Assert.That(ResultOf("TypedArrays"), Is.EqualTo("499999500000"));

        [Test]
        public void BigInt_()
            => Assert.That(ResultOf("BigInt"), Is.EqualTo("47713"));

        [Test]
        public void Sort_()
        {
            // Sort shuffles random data; only verify a numeric result in [0, 1)
            var result = ResultOf("Sort");
            Assert.That(double.TryParse(result,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var d), Is.True, $"Expected a number, got: {result}");
            Assert.That(d, Is.GreaterThanOrEqualTo(0.0).And.LessThan(1.0),
                "Sort result should be in [0, 1)");
        }

        [Test]
        public void Date_()
            => Assert.That(ResultOf("Date"), Is.EqualTo("173685591705600000"));

        [Test]
        public void Spread_()
            => Assert.That(ResultOf("Spread"), Is.EqualTo("99999"));

        [Test]
        public void AllPass()
        {
            Assert.That(s_errors, Is.Empty,
                s_errors.Count > 0
                    ? $"Benchmark errors:\n{string.Join('\n', s_errors)}"
                    : string.Empty);
            Assert.That(s_lines.Count, Is.EqualTo(15), "Expected 15 benchmark output lines");
        }
    }
}
