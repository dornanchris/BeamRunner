using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using BeamRunner.Core.Localization;
using BeamRunner.Core.Meta;
using NUnit.Framework;

namespace BeamRunner.Core.Tests
{
    /// <summary>
    /// The pure localization core (Core/Localization): CLDR plural rules per shipped language, named-argument formatting,
    /// locale number formats (independent of the device culture), fallbacks, the JSON table reader, the pseudo-localization,
    /// and every shipped table (parses, same keys and placeholders as English, right plural categories, catalog texts
    /// resolve in every language).
    /// </summary>
    public class LocalizationTests
    {
        private const string Nbsp = " ";

        [TearDown]
        public void RestoreEnglish()
        {
            Loc.Reset();
            LocTestSupport.InstallEnglish();
        }

        // ------------------------------------------------------------------ plural rules

        private static void AssertPlural(string lang, PluralCategory expected, params long[] numbers)
        {
            foreach (long n in numbers)
            {
                Assert.AreEqual(expected, PluralRules.Select(lang, n), lang + " " + n);
            }
        }

        [Test]
        public void Plural_EnglishAndGerman_OneIsOnlyOne()
        {
            foreach (string lang in new[] { "en", "de", "qps-ploc" })
            {
                AssertPlural(lang, PluralCategory.One, 1, -1);
                AssertPlural(lang, PluralCategory.Other, 0, 2, 11, 21, 101, 1000000);
            }
        }

        [Test]
        public void Plural_SpanishAndItalian_ManyForExactMillions()
        {
            foreach (string lang in new[] { "es", "it" })
            {
                AssertPlural(lang, PluralCategory.One, 1);
                AssertPlural(lang, PluralCategory.Other, 0, 2, 21, 999999, 1000001, 1500000);
                AssertPlural(lang, PluralCategory.Many, 1000000, 2000000, 1000000000);
            }
        }

        [Test]
        public void Plural_FrenchAndBrazilianPortuguese_ZeroIsSingular()
        {
            foreach (string lang in new[] { "fr", "pt-BR" })
            {
                AssertPlural(lang, PluralCategory.One, 0, 1);
                AssertPlural(lang, PluralCategory.Other, 2, 21, 1000001);
                AssertPlural(lang, PluralCategory.Many, 1000000, 3000000);
            }
        }

        [Test]
        public void Plural_Russian_OneFewMany()
        {
            AssertPlural("ru", PluralCategory.One, 1, 21, 31, 101, 1001, 100021);
            AssertPlural("ru", PluralCategory.Few, 2, 3, 4, 22, 23, 24, 102, 1004);
            AssertPlural("ru", PluralCategory.Many, 0, 5, 9, 10, 11, 12, 13, 14, 15, 19, 20, 25, 100, 111, 112, 114, 1000000);
        }

        [Test]
        public void Plural_Polish_OneFewMany()
        {
            AssertPlural("pl", PluralCategory.One, 1);
            AssertPlural("pl", PluralCategory.Few, 2, 3, 4, 22, 23, 24, 102, 1004);
            AssertPlural("pl", PluralCategory.Many, 0, 5, 11, 12, 13, 14, 21, 25, 31, 101, 111, 112, 1000000);
        }

        [Test]
        public void Plural_Turkish_OneOther_AndCjkOtherOnly()
        {
            AssertPlural("tr", PluralCategory.One, 1);
            AssertPlural("tr", PluralCategory.Other, 0, 2, 21);
            foreach (string lang in new[] { "ja", "ko", "zh-Hans" })
            {
                AssertPlural(lang, PluralCategory.Other, 0, 1, 2, 5, 21, 1000000);
            }
        }

        [Test]
        public void Plural_ExtremesDoNotThrow()
        {
            foreach (LanguageInfo lang in Languages.Shipped)
            {
                Assert.DoesNotThrow(() => PluralRules.Select(lang.Code, long.MinValue), lang.Code);
                Assert.DoesNotThrow(() => PluralRules.Select(lang.Code, long.MaxValue), lang.Code);
            }
        }

        [Test]
        public void Plural_RequiredCategories_PerLanguage()
        {
            CollectionAssert.AreEquivalent(new[] { PluralCategory.One, PluralCategory.Other }, PluralRules.RequiredCategories("de"));
            CollectionAssert.AreEquivalent(new[] { PluralCategory.One, PluralCategory.Few, PluralCategory.Many, PluralCategory.Other },
                PluralRules.RequiredCategories("pl"));
            CollectionAssert.AreEquivalent(new[] { PluralCategory.Other }, PluralRules.RequiredCategories("ja"));
            CollectionAssert.AreEquivalent(new[] { PluralCategory.Other }, PluralRules.RequiredCategories("tr"));
            Assert.IsTrue(PluralRules.IsAllowed("es", PluralCategory.Many));
            Assert.IsFalse(PluralRules.IsAllowed("de", PluralCategory.Many));
            Assert.IsFalse(PluralRules.IsAllowed("zh-Hans", PluralCategory.One));
        }

        // ------------------------------------------------------------------ numbers

        [Test]
        public void Integer_GroupsPerLanguage()
        {
            Assert.AreEqual("0", LocFormat.Integer(0, Languages.English));
            Assert.AreEqual("999", LocFormat.Integer(999, Languages.English));
            Assert.AreEqual("1,000", LocFormat.Integer(1000, Languages.English));
            Assert.AreEqual("1,234,567", LocFormat.Integer(1234567, Languages.English));
            Assert.AreEqual("-1,234", LocFormat.Integer(-1234, Languages.English));
            Assert.AreEqual("1.234.567", LocFormat.Integer(1234567, Languages.Find("de")));
            Assert.AreEqual("12.345", LocFormat.Integer(12345, Languages.Find("tr")));
            Assert.AreEqual("1" + Nbsp + "234", LocFormat.Integer(1234, Languages.Find("ru")));
            Assert.AreEqual("12" + Nbsp + "345", LocFormat.Integer(12345, Languages.Find("fr")));
            Assert.AreEqual("12,345", LocFormat.Integer(12345, Languages.Find("ja")));
        }

        [Test]
        public void Integer_MinimumGroupingDigits_SpanishAndPolish()
        {
            Assert.AreEqual("1234", LocFormat.Integer(1234, Languages.Find("es")));
            Assert.AreEqual("12.345", LocFormat.Integer(12345, Languages.Find("es")));
            Assert.AreEqual("1234", LocFormat.Integer(1234, Languages.Find("pl")));
            Assert.AreEqual("12" + Nbsp + "345", LocFormat.Integer(12345, Languages.Find("pl")));
            Assert.AreEqual("1" + Nbsp + "234" + Nbsp + "567", LocFormat.Integer(1234567, Languages.Find("pl")));
        }

        [Test]
        public void Integer_Extremes()
        {
            Assert.AreEqual("-9,223,372,036,854,775,808", LocFormat.Integer(long.MinValue, Languages.English));
            Assert.AreEqual("9,223,372,036,854,775,807", LocFormat.Integer(long.MaxValue, Languages.English));
        }

        [Test]
        public void Decimal_SeparatorsAndRounding()
        {
            Assert.AreEqual("1.5", LocFormat.Decimal(1.5, 1, Languages.English));
            Assert.AreEqual("1,5", LocFormat.Decimal(1.5, 1, Languages.Find("de")));
            Assert.AreEqual("2.3", LocFormat.Decimal(2.25, 1, Languages.English), "half away from zero");
            Assert.AreEqual("1" + Nbsp + "234,5", LocFormat.Decimal(1234.5, 1, Languages.Find("fr")));
            Assert.AreEqual("0.0", LocFormat.Decimal(-0.04, 1, Languages.English), "no negative zero");
            Assert.AreEqual("-1.2", LocFormat.Decimal(-1.2, 1, Languages.English));
            Assert.AreEqual("12", LocFormat.Decimal(12.4, 0, Languages.English));
            Assert.AreEqual("0.0", LocFormat.Decimal(double.NaN, 1, Languages.English));
        }

        [Test]
        public void Numbers_IgnoreTheDeviceCulture()
        {
            CultureInfo saved = CultureInfo.CurrentCulture;
            try
            {
                foreach (string culture in new[] { "de-DE", "tr-TR", "fr-FR", "ar-SA", "hi-IN" })
                {
                    CultureInfo c;
                    try
                    {
                        c = CultureInfo.GetCultureInfo(culture);
                    }
                    catch (CultureNotFoundException)
                    {
                        continue; // invariant-globalization runtimes
                    }

                    CultureInfo.CurrentCulture = c;
                    Assert.AreEqual("12,345", LocFormat.Integer(12345, Languages.English), culture);
                    Assert.AreEqual("1.5", LocFormat.Decimal(1.5, 1, Languages.English), culture);
                    Assert.AreEqual("12.345", LocFormat.Integer(12345, Languages.Find("de")), culture);
                    // Save/leaderboard keys are culture-invariant too.
                    Assert.AreEqual("2026-03-10", DailyRewards.DateKey(new DateTime(2026, 3, 10)), culture);
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = saved;
            }
        }

        // ------------------------------------------------------------------ formatting

        [Test]
        public void Apply_NamedArguments()
        {
            var args = new (string, object)[] { ("n", 12345), ("name", "ARROW") };
            Assert.AreEqual("12,345 M AHEAD", LocFormat.Apply("{n} M AHEAD", Languages.English, args));
            Assert.AreEqual("ARROW 12.345 ARROW", LocFormat.Apply("{name} {n} {name}", Languages.Find("de"), args));
            Assert.AreEqual("{missing} 12,345", LocFormat.Apply("{missing} {n}", Languages.English, args), "unknown placeholder stays");
            Assert.AreEqual("{ n} {n } {} {1}", LocFormat.Apply("{ n} {n } {} {1}", Languages.English, args), "not placeholders");
            Assert.AreEqual("100 %", LocFormat.Apply("{p} %", Languages.English, new (string, object)[] { ("p", "100") }));
            Assert.AreEqual("plain", LocFormat.Apply("plain", Languages.English, args));
            Assert.AreEqual(string.Empty, LocFormat.Apply(null, Languages.English, args));
        }

        [Test]
        public void Placeholders_AreExtractedInOrder()
        {
            CollectionAssert.AreEqual(new[] { "day", "n" }, LocFormat.Placeholders("Claim day {day}: {n} gems, {n}!"));
            CollectionAssert.IsEmpty(LocFormat.Placeholders("{ nope } {1x}"));
        }

        // ------------------------------------------------------------------ JSON / tables

        [Test]
        public void Json_ParsesObjectsStringsEscapesAndBom()
        {
            object root = LocJson.Parse("﻿{\"a\": \"x\\u00A0y\\n\\\"q\\\"\", \"b\": {\"one\": \"1\"}, \"c\": [true, false, null, -1.5e2]}");
            var obj = (LocJson.JsonObject)root;
            object a;
            Assert.IsTrue(obj.TryGet("a", out a));
            Assert.AreEqual("x y\n\"q\"", a);
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, obj.Keys);
        }

        [Test]
        public void Json_RejectsSloppyInput()
        {
            foreach (string bad in new[]
                     {
                         "{\"a\": \"x\",}", "{\"a\": \"x\", \"a\": \"y\"}", "{\"a\": \"x\"} // c", "{\"a\": \"x}",
                         "{a: \"x\"}", "{\"a\": \"x\\q\"}", "{\"a\": \"line\nbreak\"}", "[1, 2", "{} {}", "",
                     })
            {
                object v;
                string error;
                Assert.IsFalse(LocJson.TryParse(bad, out v, out error), bad);
                StringAssert.StartsWith("JSON:", error);
            }
        }

        [Test]
        public void Table_ReadsMetaSimpleAndPluralEntries()
        {
            StringTable t = StringTable.FromJson("ru",
                "{\"_meta\": {\"language\": \"ru\", \"machineTranslated\": true}, \"a\": \"A\", " +
                "\"p\": {\"one\": \"{n} осколок\", \"few\": \"{n} осколка\", \"many\": \"{n} осколков\", \"other\": \"{n} осколка\"}}");
            Assert.AreEqual("true", t.Meta["machineTranslated"]);
            Assert.AreEqual(2, t.Count);
            Assert.IsFalse(t.IsPlural("a"));
            Assert.IsTrue(t.IsPlural("p"));
            string s;
            Assert.IsTrue(t.TryGet("p", PluralCategory.Few, out s));
            Assert.AreEqual("{n} осколка", s);
            Assert.IsTrue(t.TryGet("a", PluralCategory.Many, out s), "a simple string answers any category");
            Assert.AreEqual("A", s);
            Assert.IsFalse(t.TryGet("missing", out s));
        }

        [Test]
        public void Table_RejectsBadPlurals()
        {
            string error;
            Assert.IsNull(StringTable.TryFromJson("x", "{\"p\": {\"one\": \"a\"}}", out error), "no other");
            StringAssert.Contains("other", error);
            Assert.IsNull(StringTable.TryFromJson("x", "{\"p\": {\"single\": \"a\", \"other\": \"b\"}}", out error));
            Assert.IsNull(StringTable.TryFromJson("x", "{\"p\": 3}", out error));
            Assert.IsNull(StringTable.TryFromJson("x", "[\"p\"]", out error));
        }

        // ------------------------------------------------------------------ localizer & fallbacks

        private static StringTable Tiny(string code, string json)
        {
            return StringTable.FromJson(code, json);
        }

        [Test]
        public void Localizer_FallsBackToEnglishThenKey_ReportingOnce()
        {
            StringTable en = Tiny("en", "{\"a\": \"A\", \"b\": \"B {n}\", \"p\": {\"one\": \"{n} gem\", \"other\": \"{n} gems\"}}");
            StringTable ru = Tiny("ru", "{\"a\": \"А\"}");
            var reported = new List<string>();
            var loc = new Localizer(en) { MissingKey = (lang, key) => reported.Add(lang + ":" + key) };
            loc.SetLanguage(Languages.Find("ru"), ru);

            Assert.AreEqual("А", loc.Get("a"));
            Assert.AreEqual("B 1" + Nbsp + "234", loc.Format("b", new (string, object)[] { ("n", 1234) }), "English text, Russian digits");
            Assert.AreEqual("21 gems", loc.Format("p", new (string, object)[] { ("n", 21) }), "English fallback uses English plural rules");
            Assert.AreEqual("1 gem", loc.Format("p", new (string, object)[] { ("n", 1) }));
            Assert.AreEqual("nope.key", loc.Get("nope.key"));
            loc.Get("nope.key");
            loc.Get("b");
            CollectionAssert.AreEqual(new[] { "ru:b", "ru:p", "ru:nope.key" }, reported, "each missing key once");
            Assert.AreEqual(string.Empty, loc.Get(null));
        }

        [Test]
        public void Localizer_PluralPicksFormFromN()
        {
            StringTable en = Tiny("en", "{\"p\": {\"one\": \"{n} gem\", \"other\": \"{n} gems\"}}");
            StringTable ru = Tiny("ru", "{\"p\": {\"one\": \"{n} осколок\", \"few\": \"{n} осколка\", \"many\": \"{n} осколков\", " +
                                        "\"other\": \"{n} осколка\"}}");
            var loc = new Localizer(en);
            loc.SetLanguage(Languages.Find("ru"), ru);
            Assert.AreEqual("1 осколок", loc.Format("p", new (string, object)[] { ("n", 1) }));
            Assert.AreEqual("21 осколок", loc.Format("p", new (string, object)[] { ("n", 21L) }));
            Assert.AreEqual("3 осколка", loc.Format("p", new (string, object)[] { ("n", 3) }));
            Assert.AreEqual("11 осколков", loc.Format("p", new (string, object)[] { ("n", 11) }));
            Assert.AreEqual("12" + Nbsp + "345 осколков", loc.Format("p", new (string, object)[] { ("n", 12345) }));
            Assert.AreEqual("{n} осколка", loc.Get("p"), "no count: the other form");
            Assert.AreEqual("{n} осколка", loc.Format("p", new (string, object)[] { ("x", 1) }), "no n: other");
        }

        [Test]
        public void Localizer_MissingTableFallsBackToEnglish_PseudoIsGenerated()
        {
            StringTable en = Tiny("en", "{\"menu.play\": \"PLAY\", \"hud.ahead\": \"{n} M AHEAD\"}");
            var loc = new Localizer(en);
            loc.SetLanguage(Languages.Find("de"), null);
            Assert.AreEqual("PLAY", loc.Get("menu.play"));
            loc.SetLanguage(Languages.Pseudo, null);
            string pseudo = loc.Get("menu.play");
            StringAssert.StartsWith("[", pseudo);
            StringAssert.DoesNotContain("PLAY", pseudo);
            Assert.AreEqual(Languages.Pseudo, loc.Language);
            StringAssert.Contains("1,234", loc.Format("hud.ahead", new (string, object)[] { ("n", 1234) }));
        }

        [Test]
        public void Loc_WithoutTablesReturnsKeys_AndRaisesLanguageChangedOnlyOnChange()
        {
            Loc.Reset();
            Assert.AreEqual("menu.play", Loc.T("menu.play"));
            Assert.AreEqual("12,345", Loc.N(12345L));
            Assert.AreEqual("3", Loc.N(3.99));
            int raised = 0;
            Loc.LanguageChanged += () => raised++;
            Loc.LanguageChanged += () => throw new InvalidOperationException("listeners must not stop each other");
            Loc.LanguageChanged += () => raised++;
            Loc.SetLanguage(Languages.English, null);
            Assert.AreEqual(0, raised, "same language");
            Loc.SetLanguage(Languages.Find("de"), Tiny("de", "{\"menu.play\": \"SPIELEN\"}"));
            Assert.AreEqual(2, raised);
            Assert.AreEqual("SPIELEN", Loc.T("menu.play"));
            Assert.AreEqual("12.345", Loc.N(12345L));
            Assert.AreEqual("1,5", Loc.N(1.5, 1));
        }

        // ------------------------------------------------------------------ languages

        [Test]
        public void Languages_DeviceLanguageMapping()
        {
            Assert.AreEqual("de", Languages.FromSystemLanguage("German").Code);
            Assert.AreEqual("pt-BR", Languages.FromSystemLanguage("Portuguese").Code);
            Assert.AreEqual("zh-Hans", Languages.FromSystemLanguage("Chinese").Code);
            Assert.AreEqual("zh-Hans", Languages.FromSystemLanguage("ChineseSimplified").Code);
            Assert.AreEqual("zh-Hans", Languages.FromSystemLanguage("ChineseTraditional").Code);
            Assert.AreEqual("en", Languages.FromSystemLanguage("Dutch").Code);
            Assert.AreEqual("en", Languages.FromSystemLanguage("Unknown").Code);
            Assert.AreEqual("en", Languages.FromSystemLanguage(null).Code);
        }

        [Test]
        public void Languages_ShippedList()
        {
            Assert.AreEqual(12, Languages.Shipped.Count);
            Assert.AreSame(Languages.English, Languages.Shipped[0]);
            Assert.AreEqual(Languages.Shipped.Count, Languages.Shipped.Select(l => l.Code).Distinct().Count());
            Assert.AreSame(Languages.Find("zh-Hans"), Languages.Find("ZH-hans"));
            Assert.AreSame(Languages.Pseudo, Languages.Find("qps-ploc"));
            Assert.IsNull(Languages.Find("xx"));
            Assert.AreEqual("pt", Languages.BaseLanguage("pt-BR"));
            Assert.AreEqual(LocScript.Cyrillic, Languages.Find("ru").Script);
            Assert.AreEqual(LocScript.Latin, Languages.Find("tr").Script);
        }

        // ------------------------------------------------------------------ pseudo-localization

        [Test]
        public void Pseudo_ExpandsAccentsAndKeepsPlaceholdersAndTags()
        {
            string p = PseudoLocalizer.Transform("<color=#FF00FF>BEST</color> {score} in one run");
            StringAssert.Contains("<color=#FF00FF>", p);
            StringAssert.Contains("</color>", p);
            StringAssert.Contains("{score}", p);
            Assert.IsFalse(Regex.IsMatch(Regex.Replace(p, "<[^>]*>|\\{[a-z]+\\}", string.Empty), "[A-Za-z]"),
                "every letter outside tags/placeholders is replaced: " + p);
            StringAssert.StartsWith("[", p);
            StringAssert.EndsWith("]", p);
            string plain = "Collect 40 gems in one run";
            Assert.GreaterOrEqual(PseudoLocalizer.Transform(plain).Length, (int)(plain.Length * 1.3f));
            Assert.AreEqual(string.Empty, PseudoLocalizer.Transform(null));
        }

        // ------------------------------------------------------------------ shipped tables

        private static IEnumerable<string> ShippedCodes()
        {
            return Languages.Shipped.Select(l => l.Code);
        }

        private static HashSet<string> PlaceholderUnion(StringTable t, string key)
        {
            var set = new HashSet<string>();
            string[] forms = t.GetForms(key);
            if (forms == null)
            {
                string s;
                t.TryGet(key, out s);
                set.UnionWith(LocFormat.Placeholders(s));
                return set;
            }

            foreach (string f in forms)
            {
                if (f != null)
                {
                    set.UnionWith(LocFormat.Placeholders(f));
                }
            }

            return set;
        }

        [Test]
        public void ShippedTables_ParseWithMetaAndTheSameKeysAsEnglish()
        {
            StringTable en = LocTestSupport.Table("en");
            Assert.Greater(en.Count, 300);
            foreach (string code in ShippedCodes())
            {
                StringTable t = LocTestSupport.Table(code);
                Assert.AreEqual(code, t.Meta["language"], code);
                if (code != "en")
                {
                    Assert.AreEqual("true", t.Meta["machineTranslated"], code + " must say it is machine-translated");
                }

                Assert.AreEqual(Languages.Find(code).DisplayName, t.Meta["nativeName"], code);
                CollectionAssert.AreEquivalent(en.Keys, t.Keys, code + ": same keys as English");
            }
        }

        [Test]
        public void ShippedTables_PlaceholdersMatchEnglish()
        {
            StringTable en = LocTestSupport.Table("en");
            foreach (string code in ShippedCodes())
            {
                StringTable t = LocTestSupport.Table(code);
                foreach (string key in en.Keys)
                {
                    HashSet<string> expected = PlaceholderUnion(en, key);
                    Assert.IsTrue(expected.SetEquals(PlaceholderUnion(t, key)), code + " " + key);
                    string other;
                    Assert.IsTrue(t.TryGet(key, PluralCategory.Other, out other), code + " " + key);
                    Assert.IsTrue(expected.SetEquals(LocFormat.Placeholders(other)), code + " " + key + " [other]");
                }
            }
        }

        [Test]
        public void ShippedTables_PluralFormsFollowCldr()
        {
            StringTable en = LocTestSupport.Table("en");
            foreach (string code in ShippedCodes())
            {
                StringTable t = LocTestSupport.Table(code);
                PluralCategory[] required = PluralRules.RequiredCategories(code);
                bool otherOnly = required.Length == 1;
                foreach (string key in en.Keys)
                {
                    string[] forms = t.GetForms(key);
                    if (en.IsPlural(key))
                    {
                        Assert.IsTrue(forms != null || otherOnly, code + " " + key + " must be plural");
                    }
                    else
                    {
                        Assert.IsNull(forms, code + " " + key + " is simple in English");
                    }

                    if (forms == null)
                    {
                        continue;
                    }

                    foreach (PluralCategory c in required)
                    {
                        Assert.IsNotNull(forms[(int)c], code + " " + key + " lacks " + PluralRules.Key(c));
                    }

                    for (int c = 0; c < forms.Length; c++)
                    {
                        if (forms[c] != null)
                        {
                            Assert.IsTrue(PluralRules.IsAllowed(code, (PluralCategory)c), code + " " + key + " has " + (PluralCategory)c);
                        }
                    }

                    // Where "one" also covers 0 or 21, 31… the count must be visible in every form.
                    if (code == "fr" || code == "pt-BR" || code == "ru")
                    {
                        foreach (string f in forms.Where(f => f != null))
                        {
                            Assert.IsTrue(PlaceholderUnion(en, key).SetEquals(LocFormat.Placeholders(f)), code + " " + key + ": " + f);
                        }
                    }
                }
            }
        }

        [Test]
        public void ShippedTables_NoRawKeysInCatalogTexts_EveryLanguage()
        {
            foreach (string code in ShippedCodes().Concat(new[] { Languages.PseudoCode }))
            {
                LocTestSupport.Install(code);
                foreach (ShipDefinition ship in ShipCatalog.All)
                {
                    AssertResolved(code, ship.DisplayName);
                    AssertResolved(code, ship.Tagline);
                    AssertResolved(code, ship.UnlockDescription);
                }

                foreach (AchievementDefinition a in AchievementCatalog.All)
                {
                    AssertResolved(code, a.Title);
                    AssertResolved(code, a.Description);
                    StringAssert.DoesNotContain("{", a.Description, code + " " + a.Id);
                }

                foreach (Field.LevelKind kind in Enum.GetValues(typeof(Field.LevelKind)))
                {
                    AssertResolved(code, Loc.T(Field.Levels.NameKey(kind)));
                }

                foreach (MissionType type in Enum.GetValues(typeof(MissionType)))
                {
                    foreach (long target in new long[] { 1, 2, 5, 21, 1500 })
                    {
                        string text = MissionSystem.Describe(new Mission { Type = (int)type, Target = target });
                        AssertResolved(code, text);
                        StringAssert.DoesNotContain("{", text, code + " " + type);
                    }
                }
            }
        }

        private static void AssertResolved(string code, string text)
        {
            Assert.IsFalse(string.IsNullOrEmpty(text), code);
            Assert.IsFalse(Regex.IsMatch(text, "^[a-z_]+(\\.[a-z0-9_]+)+$"), code + ": raw key " + text);
        }

        [Test]
        public void ShippedTables_CountsInMissionTextsAreLocalized()
        {
            LocTestSupport.Install("de");
            StringAssert.Contains("1.500", MissionSystem.Describe(new Mission { Type = (int)MissionType.DistanceRun, Target = 1500 }));
            LocTestSupport.Install("ru");
            string one = MissionSystem.Describe(new Mission { Type = (int)MissionType.GemsRun, Target = 21 });
            string many = MissionSystem.Describe(new Mission { Type = (int)MissionType.GemsRun, Target = 25 });
            StringAssert.Contains("21", one);
            StringAssert.Contains("25", many);
            Assert.AreNotEqual(one.Replace("21", "#"), many.Replace("25", "#"), "Russian 21 (one) and 25 (many) use different forms");

            // English: every mission text is a real sentence that carries its formatted target (the wording itself is the
            // string table's business, not the tests').
            LocTestSupport.Install("en");
            foreach (MissionType type in Enum.GetValues(typeof(MissionType)))
            {
                string single = MissionSystem.Describe(new Mission { Type = (int)type, Target = 1 });
                string big = MissionSystem.Describe(new Mission { Type = (int)type, Target = 12345 });
                AssertResolved("en", single);
                AssertResolved("en", big);
                StringAssert.Contains("12,345", big, type + " shows its target with English digit grouping");
                StringAssert.DoesNotContain("{", single + big, type.ToString());
                Assert.That(big.Trim().Length, Is.GreaterThan(8), type + " reads as a sentence");
                Assert.AreNotEqual(single, big, type + ": the target is part of the text");
            }
        }

        [Test]
        public void ShippedTables_ReminderTextsInEveryLanguage()
        {
            var daily = new DailyRewardState { LastClaimDate = DailyRewards.DateKey(new DateTime(2026, 9, 21)), Streak = 3 };
            foreach (string code in ShippedCodes())
            {
                LocTestSupport.Install(code);
                ReminderPlan plan = ReminderPlanner.Plan(ReminderContext.Create(new DateTime(2026, 9, 21, 10, 0, 0), daily));
                Assert.IsTrue(plan.HasReminder, code);
                AssertResolved(code, plan.Title);
                AssertResolved(code, plan.Body);
                StringAssert.DoesNotContain("{", plan.Title + plan.Body, code);
            }
        }

        [Test]
        public void PseudoTable_CoversEveryEnglishKey()
        {
            StringTable en = LocTestSupport.Table("en");
            StringTable pseudo = PseudoLocalizer.BuildTable(en);
            CollectionAssert.AreEqual(en.Keys, pseudo.Keys);
            foreach (string key in en.Keys)
            {
                Assert.AreEqual(en.IsPlural(key), pseudo.IsPlural(key), key);
                Assert.IsTrue(PlaceholderUnion(en, key).SetEquals(PlaceholderUnion(pseudo, key)), key);
            }
        }
    }
}
