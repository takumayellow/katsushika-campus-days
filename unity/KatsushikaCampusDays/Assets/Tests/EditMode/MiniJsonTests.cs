using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace KCD.Tests
{
    public sealed class MiniJsonTests
    {
        [Test]
        public void Deserialize_ParsesNestedObjectsAndArrays()
        {
            const string json = "{\"a\": 1, \"b\": [true, false, null, \"x\"], \"c\": {\"d\": -2.5}}";

            var root = MiniJson.Deserialize(json) as Dictionary<string, object>;

            Assert.IsNotNull(root);
            Assert.AreEqual(1, MiniJson.GetInt(root, "a"));
            List<object> array = MiniJson.GetArray(root, "b");
            Assert.AreEqual(4, array.Count);
            Assert.AreEqual(true, array[0]);
            Assert.AreEqual(false, array[1]);
            Assert.IsNull(array[2]);
            Assert.AreEqual("x", array[3]);
            Assert.AreEqual(-2.5f, MiniJson.GetFloat(MiniJson.GetObject(root, "c"), "d"), 1e-6f);
        }

        [Test]
        public void Deserialize_HandlesEscapesAndUnicode()
        {
            const string json = "{\"s\": \"line\\nbreak \\\"quoted\\\" \\u845b\\u98fe\"}";

            var root = MiniJson.Deserialize(json) as Dictionary<string, object>;

            Assert.AreEqual("line\nbreak \"quoted\" 葛飾", MiniJson.GetString(root, "s"));
        }

        [Test]
        public void Deserialize_ReturnsNullForEmptyInput()
        {
            Assert.IsNull(MiniJson.Deserialize(null));
            Assert.IsNull(MiniJson.Deserialize(string.Empty));
        }

        [Test]
        public void Getters_FallBackWhenKeyMissingOrWrongType()
        {
            var root = MiniJson.Deserialize("{\"n\": \"not a number\", \"f\": 1.25}") as Dictionary<string, object>;

            Assert.AreEqual("dflt", MiniJson.GetString(root, "missing", "dflt"));
            Assert.AreEqual(7, MiniJson.GetInt(root, "missing", 7));
            Assert.AreEqual(1, MiniJson.GetInt(root, "f", 0));
            Assert.AreEqual(false, MiniJson.GetBool(root, "n", false));
            Assert.IsNull(MiniJson.GetObject(root, "missing"));
            Assert.AreEqual(0, MiniJson.GetArray(root, "missing").Count);
        }

        [Test]
        public void Deserialize_RejectsTooDeepNesting()
        {
            string json = new string('[', 200) + new string(']', 200);

            Assert.Throws<FormatException>(() => MiniJson.Deserialize(json));
        }

        [Test]
        public void Deserialize_RejectsInvalidUnicodeEscape()
        {
            Assert.Throws<FormatException>(() => MiniJson.Deserialize("{\"k\": \"\\uZZZZ\"}"));
        }

        [Test]
        public void Getters_TolerateNullNode()
        {
            Assert.AreEqual("x", MiniJson.GetString(null, "k", "x"));
            Assert.AreEqual(3, MiniJson.GetInt(null, "k", 3));
            Assert.AreEqual(0, MiniJson.GetArray(null, "k").Count);
        }

        [Test]
        public void ToStringList_SkipsNonStrings()
        {
            var array = new List<object> { "a", 1.0, null, "b" };

            List<string> list = MiniJson.ToStringList(array);

            CollectionAssert.AreEqual(new[] { "a", "b" }, list);
        }
    }
}
