using System.Collections.Generic;
using snapvox.foundation.interfaces.Ocr;
using snapvox.helpers;
using snapvox.native.foundation;
using Xunit;

namespace snapvox.tests
{
    public class OcrTextLayoutTests
    {
        [Fact]
        public void ContainsHebrew_DetectsHebrewCharacters()
        {
            Assert.True(OcrTextLayout.ContainsHebrew("שלום עולם"));
            Assert.True(OcrTextLayout.ContainsHebrew("Hello עולם"));
            Assert.False(OcrTextLayout.ContainsHebrew("Hello World 123"));
            Assert.False(OcrTextLayout.ContainsHebrew(null!));
            Assert.False(OcrTextLayout.ContainsHebrew(""));
        }

        [Fact]
        public void ContainsLatin_DetectsLatinCharacters()
        {
            Assert.True(OcrTextLayout.ContainsLatin("Hello"));
            Assert.True(OcrTextLayout.ContainsLatin("שלום Hello"));
            Assert.False(OcrTextLayout.ContainsLatin("שלום עולם 123"));
            Assert.False(OcrTextLayout.ContainsLatin(null!));
            Assert.False(OcrTextLayout.ContainsLatin(""));
        }

        [Fact]
        public void SwapParentheses_InvertsBracketTypes()
        {
            Assert.Equal("(abc)", OcrTextLayout.SwapParentheses(")abc("));
            Assert.Equal("[123]", OcrTextLayout.SwapParentheses("]123["));
            Assert.Equal("{xyz}", OcrTextLayout.SwapParentheses("}xyz{"));
            Assert.Equal("<tag>", OcrTextLayout.SwapParentheses(">tag<"));
            Assert.Equal("plain text", OcrTextLayout.SwapParentheses("plain text"));
        }

        [Fact]
        public void BuildVisualSelectionText_HebrewRtlOrdering()
        {
            // In visual reading order from left to right: word2 is rightmost, word1 is leftmost
            // In Hebrew, text is read right to left.
            // When rightmost word is "שלום" (left=100) and leftmost is "עולם" (left=0)
            var word1 = new OcrWord { Text = "עולם", Bounds = RECT.FromXYWH(0, 0, 40, 20) };
            var word2 = new OcrWord { Text = "שלום", Bounds = RECT.FromXYWH(50, 0, 40, 20) };

            var words = new List<OcrWord> { word1, word2 };
            string text = OcrTextLayout.BuildVisualSelectionText(words);

            // In RTL dominant line, rightmost Hebrew word "שלום" comes first, followed by "עולם"
            Assert.Equal("שלום עולם", text);
        }

        [Fact]
        public void BuildVisualSelectionText_EnglishLtrOrdering()
        {
            var word1 = new OcrWord { Text = "World", Bounds = RECT.FromXYWH(60, 0, 40, 20) };
            var word2 = new OcrWord { Text = "Hello", Bounds = RECT.FromXYWH(0, 0, 40, 20) };

            var words = new List<OcrWord> { word1, word2 };
            string text = OcrTextLayout.BuildVisualSelectionText(words);

            // In LTR dominant line, leftmost word "Hello" comes first, followed by "World"
            Assert.Equal("Hello World", text);
        }

        [Fact]
        public void BuildVisualSelectionText_MultiLineGrouped()
        {
            // Line 1: y = 10
            var w1 = new OcrWord { Text = "LineOne", Bounds = RECT.FromXYWH(0, 10, 50, 20) };
            // Line 2: y = 50
            var w2 = new OcrWord { Text = "LineTwo", Bounds = RECT.FromXYWH(0, 50, 50, 20) };

            var words = new List<OcrWord> { w2, w1 };
            string text = OcrTextLayout.BuildVisualSelectionText(words);

            Assert.Contains("LineOne", text);
            Assert.Contains("LineTwo", text);
            Assert.True(text.IndexOf("LineOne") < text.IndexOf("LineTwo"));
        }

        [Fact]
        public void OcrWord_WithOffset_ReturnsNewInstanceWithoutMutatingOriginal()
        {
            var original = new OcrWord { Text = "Word", Bounds = RECT.FromXYWH(10, 20, 30, 40), Confidence = 0.95f };
            var offset = original.WithOffset(5, 10);

            Assert.NotSame(original, offset);
            Assert.Equal("Word", offset.Text);
            Assert.Equal(15, offset.Bounds.Left);
            Assert.Equal(30, offset.Bounds.Top);
            Assert.Equal(30, offset.Bounds.Width);
            Assert.Equal(40, offset.Bounds.Height);
            Assert.Equal(0.95f, offset.Confidence);

            // Verify original remained unchanged
            Assert.Equal(10, original.Bounds.Left);
            Assert.Equal(20, original.Bounds.Top);
        }

        [Fact]
        public void OcrInformation_WithOffset_ReturnsNewInstanceWithoutMutatingOriginal()
        {
            var w1 = new OcrWord { Text = "A", Bounds = RECT.FromXYWH(0, 0, 10, 10) };
            var info = new OcrInformation { Text = "A", Words = new List<OcrWord> { w1 } };

            var offsetInfo = info.WithOffset(20, 30);

            Assert.NotSame(info, offsetInfo);
            Assert.Equal(20, offsetInfo.Words[0].Bounds.Left);
            Assert.Equal(30, offsetInfo.Words[0].Bounds.Top);

            // Original is unchanged
            Assert.Equal(0, info.Words[0].Bounds.Left);
            Assert.Equal(0, info.Words[0].Bounds.Top);
        }

        [Fact]
        public void OcrInformation_Offset_HandlesNullGracefully()
        {
            var info = new OcrInformation { Text = "Empty", Words = null! };
            info.Offset(10, 10); // should not throw

            var infoWithNullWord = new OcrInformation { Text = "Test", Words = new List<OcrWord> { null! } };
            infoWithNullWord.Offset(10, 10); // should not throw
            Assert.Null(infoWithNullWord.Words[0]);
        }
    }
}
