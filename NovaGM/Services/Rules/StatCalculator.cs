using System;
using NovaGM.Services.Packs;

namespace NovaGM.Services.Rules
{
    public static class StatCalculator
    {
        public static int AbilityMod(int score) => (int)Math.Floor((score - 10) / 2.0);

        public static int HP(int level, int con, int classHitDie, RulesDoc rules)
        {
            var expr = rules.Formulas.TryGetValue("HP", out var s) ? s : "classHitDie + mod(con) * level";
            var ctx = new EvalContext()
                .With("level", level)
                .With("con", con)
                .With("classHitDie", classHitDie);
            return FormulaEngine.EvalInt(expr, ctx, rules.Constants);
        }

        public static int AC(int dex, int armor, int shield, RulesDoc rules)
        {
            var expr = rules.Formulas.TryGetValue("AC", out var s) ? s : "BaseAC + armor + shield + mod(dex)";
            var ctx = new EvalContext()
                .With("dex", dex)
                .With("armor", armor)
                .With("shield", shield);
            return FormulaEngine.EvalInt(expr, ctx, rules.Constants);
        }

        public static int AttackBonus(int str, int prof, int weaponAcc, RulesDoc rules)
        {
            var expr = rules.Formulas.TryGetValue("AttackBonus", out var s) ? s : "prof + weaponAcc + mod(str)";
            var ctx = new EvalContext()
                .With("str", str)
                .With("prof", prof)
                .With("weaponAcc", weaponAcc);
            return FormulaEngine.EvalInt(expr, ctx, rules.Constants);
        }

        public static int CarryCap(int str, RulesDoc rules)
        {
            var expr = rules.Formulas.TryGetValue("CarryCap", out var s) ? s : "15 * str";
            var ctx = new EvalContext().With("str", str);
            return FormulaEngine.EvalInt(expr, ctx, rules.Constants);
        }
    }
}
