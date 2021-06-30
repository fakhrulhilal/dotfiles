using Iontas.DataLayer;
using Iontas.KnownTypes.Classes;
using NUnit.Framework;

namespace UnitTests
{
    [TestFixture]
    class CommandLineProcessorTests
    {
        private readonly CommandLineProcessor _sut = new CommandLineProcessor();

        [Test]
        public void WhenStartedWithPercentCharacterThenItIsSwitchParameter()
        {
            var result = _sut.Parse("%FORMAT");

            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(new TriggerCommandSwitchParameter
            {
                Name = "FORMAT"
            }).Using(ClassComparer.ByPublicPropertyExcept(nameof(TriggerCommandParameter.Sequence))));
        }

        [Test]
        public void WhenSwitchParameterDoesNotFollowedBySpaceThenItWillBeIgnored()
        {
            var result = _sut.Parse("%FORMAT %TRIGNAME");

            Assert.That(result,
                Is.EqualTo(new[] {new TriggerCommandSwitchParameter {Name = "FORMAT"}})
                    .Using(ClassComparer.ByPublicPropertyExcept(nameof(TriggerCommandParameter.Sequence))));
        }

        [Test]
        public void WhenSurroundedWithLessAndGreaterSignThenItIsVariableParameter()
        {
            var result = _sut.Parse("\"<VariableName>\"");

            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(new TriggerCommandVariableParameter
            {
                Name = "VariableName"
            }).Using(ClassComparer.ByPublicPropertyExcept(nameof(TriggerCommandParameter.Sequence))));
        }

        [Test]
        public void WhenVariableParameterHasSpaceThenItShouldBeOk()
        {
            var result = _sut.Parse("\"<Variable Name>\"");

            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(new TriggerCommandVariableParameter
            {
                Name = "Variable Name"
            }).Using(ClassComparer.ByPublicPropertyExcept(nameof(TriggerCommandParameter.Sequence))));
        }

        [Test]
        public void WhenVariableParameterIsNotInsideDoubleQuoteThenItWillBeIgnored()
        {
            var result = _sut.Parse("<Variable Name>");

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void WhenSurroundedByDoubleQuoteThenItIsLiteralParameter()
        {
            var result = _sut.Parse("\"Plain text: \"");

            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(new TriggerCommandLiteralParameter
            {
                Text = "Plain text: "
            }).Using(ClassComparer.ByPublicPropertyExcept(nameof(TriggerCommandParameter.Sequence))));
        }

        [Test]
        public void WhenLiteralParameterContainsVariableParameterThenItWillBeSeparateLiteral()
        {
            var result = _sut.Parse("\"You're <Age> years old\"");

            Assert.That(result, Is.EqualTo(new TriggerCommandParameter[]
            {
                new TriggerCommandLiteralParameter("You're "), 
                new TriggerCommandVariableParameter(default(int), string.Empty, "Age"), 
                new TriggerCommandLiteralParameter(" years old")
            }).Using(ClassComparer.ByPublicPropertyExcept(nameof(TriggerCommandParameter.Sequence))));
        }
 
        [Test]
        public void WhenSurroundedWithCurlyBracesThenItIsHotKeyParameter()
        {
            var result = _sut.Parse("{HOME}");

            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(new TriggerCommandHotKeyParameter
            {
                Names = { "HOME" }
            }).Using(ClassComparer.ByPublicPropertyExcept(nameof(TriggerCommandParameter.Sequence))));
        }

        [Test]
        public void WhenHavingMultipleHotKeysThenItShouldBeSeparatedByPlusCharacter()
        {
            var result = _sut.Parse("{CTRL+ALT+DEL}");

            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(new TriggerCommandHotKeyParameter
            {
                Names = { "CTRL", "ALT", "DEL" }
            }).Using(ClassComparer.ByPublicPropertyExcept(nameof(TriggerCommandParameter.Sequence))));
        }

        [Test]
        public void WhenParsedThenSequenceWillBeCountedFromLefToRight()
        {
            var result = _sut.Parse("%FORMAT \"<VariableName><Variable 2>\"{END}");

            Assert.That(result, Is.EqualTo(new TriggerCommandParameter[]
            {
                new TriggerCommandSwitchParameter
                {
                    Name = "FORMAT",
                    Sequence = 1
                },
                new TriggerCommandVariableParameter
                {
                    Name = "VariableName",
                    Sequence = 2
                },
                new TriggerCommandVariableParameter
                {
                    Name = "Variable 2",
                    Sequence = 3
                },
                new TriggerCommandHotKeyParameter
                {
                    Sequence = 4,
                    Names = { "END" }
                } 
            }).Using(ClassComparer.ByPublicProperty));
        }
    }
}
