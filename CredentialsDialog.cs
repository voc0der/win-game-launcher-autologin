namespace UbisoftAutoLogin;

internal sealed class CredentialsDialog : Form
{
    private readonly TextBox _usernameTextBox = new();
    private readonly TextBox _passwordTextBox = new();
    private string _acceptedUsername = string.Empty;
    private string _acceptedPassword = string.Empty;

    public CredentialsDialog(string? existingUsername)
    {
        Text = "Ubisoft Auto Login Credentials";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(420, 178);
        Font = SystemFonts.MessageBoxFont;

        var usernameLabel = new Label
        {
            Text = "Username / email",
            AutoSize = true,
            Location = new Point(16, 18)
        };

        _usernameTextBox.Location = new Point(16, 42);
        _usernameTextBox.Size = new Size(388, 27);
        _usernameTextBox.Text = existingUsername ?? string.Empty;
        _usernameTextBox.TabIndex = 0;

        var passwordLabel = new Label
        {
            Text = "Password",
            AutoSize = true,
            Location = new Point(16, 78)
        };

        _passwordTextBox.Location = new Point(16, 102);
        _passwordTextBox.Size = new Size(388, 27);
        _passwordTextBox.UseSystemPasswordChar = true;
        _passwordTextBox.TabIndex = 1;

        var okButton = new Button
        {
            Text = "Save",
            DialogResult = DialogResult.OK,
            Location = new Point(248, 142),
            Size = new Size(75, 28),
            TabIndex = 2
        };

        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(329, 142),
            Size = new Size(75, 28),
            TabIndex = 3
        };

        AcceptButton = okButton;
        CancelButton = cancelButton;

        Controls.AddRange(new Control[]
        {
            usernameLabel,
            _usernameTextBox,
            passwordLabel,
            _passwordTextBox,
            okButton,
            cancelButton
        });
    }

    public string Username => _acceptedUsername;

    public string Password => _acceptedPassword;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK)
        {
            _acceptedUsername = _usernameTextBox.Text.Trim();
            _acceptedPassword = _passwordTextBox.Text;
        }

        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _passwordTextBox.Text = string.Empty;
        base.OnFormClosed(e);
    }
}
