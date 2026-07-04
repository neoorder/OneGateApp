#if ANDROID
using Android.App;
using Android.Content;
using Android.Content.Res;
using Microsoft.Maui.Handlers;

namespace NeoOrder.OneGate.Controls.Handlers;

public class OneGateDatePickerHandler : DatePickerHandler
{
    protected override DatePickerDialog CreateDatePickerDialog(int year, int month, int day)
    {
        var dialog = base.CreateDatePickerDialog(year, month, day);
        dialog.ShowEvent += OnDialogShown;
        return dialog;
    }

    static void OnDialogShown(object? sender, EventArgs e)
    {
        if (sender is not DatePickerDialog dialog)
            return;

        var textColor = ColorStateList.ValueOf(new Android.Graphics.Color(dialog.Context.GetColor(Resource.Color.colorPrimaryDark)));
        dialog.GetButton((int)DialogButtonType.Positive)?.SetTextColor(textColor);
        dialog.GetButton((int)DialogButtonType.Negative)?.SetTextColor(textColor);
        dialog.GetButton((int)DialogButtonType.Neutral)?.SetTextColor(textColor);
    }
}
#endif
