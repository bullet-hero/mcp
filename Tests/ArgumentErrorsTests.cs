using System;
using System.Text.Json;
using NUnit.Framework;

namespace BH.MCP.Tests
{
    [TestFixture]
    public class ArgumentErrorsTests
    {
        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.VeryEasy)]
        public void AWrongType_IsNamed_EvenWrappedByTheBinder()
        {
            var inner = new JsonException("The JSON value could not be converted to System.Int32. Path: $ | x", "$.id",
                null, null);

            var message = ArgumentErrors.Describe(new InvalidOperationException("binding failed", inner));

            StringAssert.Contains("wrong type at $.id", message);
            StringAssert.Contains("System.Int32", message);
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.VeryEasy)]
        public void AMissingArgument_IsNamed()
        {
            var message = ArgumentErrors.Describe(new ArgumentException(
                "The arguments dictionary is missing a value for the required parameter 'from'.", "arguments"));

            StringAssert.Contains("required parameter 'from'", message);
            StringAssert.DoesNotContain("(Parameter", message);
        }

        [Test]
        [Author(Metadata.Author.Vertoker)]
        [Category(Metadata.Category.Self)]
        [Category(Metadata.Category.VeryEasy)]
        public void AnyOtherFailure_IsNotAnArgumentError()
        {
            Assert.IsNull(ArgumentErrors.Describe(new InvalidOperationException("boom")));
        }
    }
}
