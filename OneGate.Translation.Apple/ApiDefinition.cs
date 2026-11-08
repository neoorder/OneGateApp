#nullable enable
using System;
using Foundation;
using ObjCRuntime;
using UIKit;

namespace OneGate.Translation.Apple;

[BaseType(typeof(NSObject), Name = "OGTranslationBridge")]
interface TranslationBridge
{
    [Static, Export("isSupported")]
    bool IsSupported { get; }

    [Export("getLanguagesWithCompletion:")]
    void GetLanguages(Action<string[], nint> completion);

    // Status codes are content-free and shared with ChatTranslationService.Apple.cs.
    [Export("translateText:source:target:presenter:completion:")]
    void Translate(string text, [NullAllowed] string source, string target, UIViewController presenter,
        Action<string?, string?, string?, nint> completion);

    [Export("cancel")]
    void Cancel();
}
