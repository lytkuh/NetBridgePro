using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using NetBridge.Windows.Core;

namespace NetBridge.Windows.Ui;

public partial class AboutPage : Page, NetBridge.Windows.Core.ILocalizable
{
    private const string SiteUrl = "https://lytkuh.ir";
    private const string GithubUrl = "https://github.com/lytkuh/NetBridgePro";

    public AboutPage()
    {
        InitializeComponent();
        ApplyTexts();

        try
        {
            LogoImage.Source = new BitmapImage(
                new Uri("pack://application:,,,/Assets/netbridge-logo.png"));
        }
        catch { /* logo is decorative — never block startup */ }
    }

    public void ApplyTexts()
    {
        FlowDirection = I18N.Dir; // pages do not inherit RTL from the Frame-hosted window
        TaglineText.Text = I18N.T("about_tagline");
        VersionText.Text = $"{I18N.T("about_version")} " +
                           (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.2.0");
        WhatTitle.Text = I18N.T("about_what_title");
        WhatBody.Text = I18N.T("about_what_body");
        WhatBody2.Text = I18N.T("about_what_body2");
        CreditsTitle.Text = I18N.T("about_credits_title");
        DevLabel.Text = I18N.T("about_developer");
        DevValue.Text = I18N.T("about_developer_name");
        SiteLabel.Text = I18N.T("about_website");
        SiteValue.Text = "lytkuh.ir";
        GithubLabel.Text = I18N.T("about_github");
        GithubValue.Text = "github.com/lytkuh/NetBridgePro";
        PoweredLabel.Text = I18N.T("about_powered");
        PoweredValue.Text = I18N.T("about_powered_name");
        SubsidiaryLabel.Text = I18N.T("about_subsidiary");
        SubsidiaryValue.Text = I18N.T("about_subsidiary_name");
        FooterText.Text = I18N.T("about_footer");
    }

    private void Site_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(SiteUrl) { UseShellExecute = true }); }
        catch { /* no browser — ignore */ }
    }

    private void Github_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(GithubUrl) { UseShellExecute = true }); }
        catch { /* no browser — ignore */ }
    }
}
