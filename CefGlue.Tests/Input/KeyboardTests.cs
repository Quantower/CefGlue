using Avalonia.Input;
using NUnit.Framework;
using System.Threading.Tasks;

namespace CefGlue.Tests.Input
{
    public class KeyboardTests : TestBase
    {
        [Test]
        public async Task TypedCharacterReachesTextArea()
        {
            await LoadFocusedTextArea();

            await Run(() => TypeText("a"));

            Assert.AreEqual("a", await WaitForTextAreaValueEndingWith("a"));
        }

        [Test]
        public async Task EnterInsertsNewLineInTextArea()
        {
            await LoadFocusedTextArea();

            // Chromium handles input in order, so once "a" lands the Enter before it has been handled too.
            await Run(() =>
            {
                PressKey(Key.Enter);
                TypeText("a");
            });

            Assert.AreEqual("\na", await WaitForTextAreaValueEndingWith("a"));
        }

        private async Task LoadFocusedTextArea()
        {
            await Browser.LoadContent("<html><body><textarea></textarea></body></html>");
            await Run(() =>
            {
                Browser.Focusable = true;
                Browser.Focus();
            });
            await EvaluateJavascript<bool>("document.querySelector('textarea').focus(); return true;");
        }

        private void PressKey(Key key)
        {
            Browser.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
            Browser.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key });
        }

        private void TypeText(string text)
        {
            Browser.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text });
        }

        private async Task<string> WaitForTextAreaValueEndingWith(string suffix)
        {
            var value = "";
            for (var attempt = 0; attempt < 100 && !value.EndsWith(suffix); attempt++)
            {
                await Task.Delay(50);
                value = await EvaluateJavascript<string>("return document.querySelector('textarea').value;");
            }

            return value;
        }
    }
}
