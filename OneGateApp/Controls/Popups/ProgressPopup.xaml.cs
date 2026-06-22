namespace NeoOrder.OneGate.Controls.Popups;

public partial class ProgressPopup : MyPopup<bool>
{
    public required string Title { get; set { field = value; OnPropertyChanged(); } }
    public required string Message { get; set { field = value; OnPropertyChanged(); } }

    public ProgressPopup()
    {
        InitializeComponent();
    }
}
