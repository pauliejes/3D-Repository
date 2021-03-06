using System;
using System.Collections;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Web;
using System.Web.Security;
using System.Web.UI;
using System.Web.UI.HtmlControls;
using System.Web.UI.WebControls;
using System.Web.UI.WebControls.WebParts;
using System.Xml.Linq;
using System.Collections.Generic;
using vwarDAL;
using Website;
using System.Reflection;
using System.Web.Script.Services;
using System.Web.Script.Serialization;
public partial class Public_Results : Website.Pages.PageBase
{
    const int DEFAULT_RESULTS_PER_PAGE = 5;

    private int _ResultsPerPage = 6;
    private int _PageNumber = 1;
    private FederateRecordSet federates;
    private List<SearchResult> SearchResults;
    [Serializable]
    class SearchResult
    {
        public string PID { get; set; }
        public string Title { get; set; }
        public string OrganizationName { get; set; }
        public string OrganizationURL { get; set; }
    }
    protected void Page_Load(object sender, EventArgs e)
    {
        SearchPanel.Visible = false;


        if (!IsPostBack)
        {
            if (Request.QueryString["SearchTerms"] != "" && Request.QueryString["SearchTerms"] != null)
            {
                ViewState["Federates"] = null;
                ViewState["SearchResults"] = null;
                SearchResults = null;
                ApplySearchResults(GetSearchResults(Request.QueryString["SearchTerms"]));
                SearchFederationTextBox.Text = Request.QueryString["SearchTerms"];
                SearchResultsUpdatePanel.Visible = true;
               
            }


        }
        else
        {
            _ResultsPerPage = System.Convert.ToInt16(ResultsPerPageDropdown.SelectedValue);
        }

        
    }


    private IEnumerable<SearchResult> GetSearchResults(string terms)
    {
        if (ViewState["SearchResults"] == null)
        {
            SearchResults = new List<SearchResult>();

            System.Net.WebClient wc = new System.Net.WebClient();
            wc.Credentials = new System.Net.NetworkCredential("AnonymousUser", "");
            string data = wc.DownloadString("http://3dr.adlnet.gov/Federation/3DR_Federation.svc/Search/" + terms + "/json?ID=00-00-00");
            SearchResults.Clear();
            SearchResult[] results = (new JavaScriptSerializer()).Deserialize<SearchResult[]>(data);
            foreach (SearchResult s in results)
            {
                SearchResults.Add(s);
            }
            ViewState["SearchResults"] = SearchResults;
        }
        return ViewState["SearchResults"] as List<SearchResult>;
    }
    protected void RefreshSearch(object sender, EventArgs args)
    {
        if (Request.QueryString["SearchTerms"] != "")
        {
             ApplySearchResults(GetSearchResults(Request.QueryString["SearchTerms"]));
        }
        else
        {
            ApplySearchResults(GetSearchResults(SearchFederationTextBox.Text));
        }
    }
    protected void SearchFederatonButton_Click(object sender, EventArgs args)
    {

        Response.Redirect("~/Public/FederationResults.aspx?SearchTerms=" + SearchFederationTextBox.Text);
           // ApplySearchResults(GetSearchResults(SearchFederationTextBox.Text));
        
    }
    private void ApplySearchResults(IEnumerable<SearchResult> co)
    {
        List<SearchResult> results = new List<SearchResult>();
        for (int i = (_PageNumber - 1) * _ResultsPerPage; i < co.Count() && i < (_PageNumber - 1) * _ResultsPerPage + _ResultsPerPage; i++)
        {
            try
            {
                co.ElementAt(i).OrganizationName = GetFederateInfo(PidToNamespace(co.ElementAt(i).PID)).OrginizationName;
                co.ElementAt(i).OrganizationURL = GetFederateInfo(PidToNamespace(co.ElementAt(i).PID)).OrganizationURL;
            }
            catch (Exception e)
            {
                //it's possible that the federate info for a result is null. Should not happen, but totally possible if fedreates
                //have PID's in their search results that are unregistered namespaces
            }
            results.Add(co.ElementAt(i));
        }

        SearchList.DataSource = results;
        SearchList.DataBind();
        BindPageNumbers(co.Count());
        Client_UpdateSelectedPageNumber();
    }
    protected void BackButton_Click(object sender, EventArgs e)
    {
        string url = Request.ServerVariables["HTTP_REFERER"].ToString();

        if (Request.QueryString["ContentObjectID"] != null && !string.IsNullOrEmpty(Request.QueryString["ContentObjectID"].ToString()))
        {
            string coid = Server.UrlDecode(Request.QueryString["ContentObjectID"].ToString().Trim());

            url = Website.Pages.Types.FormatModel(coid);

        }

        Response.Redirect(url);
    }
    public enum FederateState { Active, Offline, Unapproved, Banned, Unknown, Delisted };
    [Serializable]
    public class FederateRecord
    {

        public string RESTAPI;
        public string SOAPAPI;
        public string namespacePrefix;
        public string OrginizationName;
        public string OrganizationURL;
        public string OrganizationPOC;
        public string OrganizationPOCEmail;
        public string OrganizationPOCPassword;
        public FederateState ActivationState;
        public bool AllowFederatedSearch;
        public bool AllowFederatedDownload;
    }
    [Serializable]
    public class FederateRecordSet
    {
        public List<FederateRecord> federates;
    }
    public FederateRecordSet GetFederateInfo()
    {
        if (ViewState["Federates"] == null)
        {
            System.Net.WebClient wc = new System.Net.WebClient();
            string federatedata = wc.UploadString("http://3dr.adlnet.gov/federation/3DR_Federation_Mgmt.svc/GetAllFederates", "POST", "");
            federates = (new JavaScriptSerializer()).Deserialize<FederateRecordSet>(federatedata);
            ViewState["Federates"] = federates;
        }
        return ViewState["Federates"] as FederateRecordSet;
    }
    public FederateRecord GetFederateInfo(string pid)
    {
        foreach (FederateRecord f in GetFederateInfo().federates)
        {
            if (String.Equals(f.namespacePrefix, pid, StringComparison.CurrentCultureIgnoreCase))
                return f;
        }
        return null;
    }
    public string PidToNamespace(string pid)
    {
        string nameSpace = null;
        pid = HttpUtility.UrlDecode(pid);
        int colon = pid.IndexOfAny(new char[] { ':', '_' });
        nameSpace = pid.Substring(0, colon);
        return nameSpace;
    }
    protected void BindPageNumbers(int numResults)
    {
        int numPages = Math.Max((int)Math.Ceiling(numResults / (float)_ResultsPerPage), 1);
        int range = Math.Min(10, numPages);
        int start = 0;

        //Determine the page numbers to add, based on the range
        if (_PageNumber <= System.Convert.ToInt32(range * 0.5f))
            start = 1;
        else if (_PageNumber >= System.Convert.ToInt32(numPages - range * 0.5f))
            start = numPages - range + 1;
        else
            start = _PageNumber - System.Convert.ToInt32(0.5f * range);

        int[] datasource = new int[range];

        int i = 0;
        for (int j = start; j <= start + range - 1; j++)
            datasource[i++] = j;

        PageNumbersRepeater.DataSource = datasource;
        PageNumbersRepeater.DataBind();

        UpdateResultsLabel((_PageNumber-1) * _ResultsPerPage + 1, numResults);
        UpdatePreviousNextButtons(numPages);
    }
    protected void PageNumberChanged(object sender, EventArgs e)
    {
        //Get the page number from the value displayed to the user
        LinkButton btn = (LinkButton)sender;
        if (btn.CommandArgument == "Next")
            _PageNumber += 1;
        else if (btn.CommandArgument == "Prev")
            _PageNumber -= 1;
        else
            _PageNumber = System.Convert.ToInt32(btn.CommandArgument);

        if (_PageNumber < 1) _PageNumber = 1;

        IEnumerable<SearchResult> co = null;
        if (Request.QueryString["SearchTerms"] != "")
        {
            co = GetSearchResults(Request.QueryString["SearchTerms"]);
        }
        else
        {
            co = GetSearchResults(SearchFederationTextBox.Text);
        }

       
        ApplySearchResults(co);

        BindPageNumbers(co.Count());
                        
    }
    protected void UpdatePreviousNextButtons(int numPages)
    {
        PreviousPageButton.Visible = _PageNumber > 1;
        NextPageButton.Visible = _PageNumber < numPages;

        if (PreviousPageButton.Visible)
            PreviousPageButton.CommandArgument = (_PageNumber - 1).ToString();

        if (NextPageButton.Visible)
            NextPageButton.CommandArgument = (_PageNumber + 1).ToString();
    }
    protected void UpdateResultsLabel(int start, int numResults)
    {
        string resultsTemplate = "Showing results {0}-{1} of {2}";
        int end = start + _ResultsPerPage - 1;
        if (end > numResults)
            end = numResults;
        ResultsLabel.Text = String.Format(resultsTemplate, start, end, numResults);
    }
    private void Client_UpdateSelectedPageNumber()
    {
        ScriptManager.RegisterClientScriptBlock(this, Page.GetType(), "updatepgnum", "UpdateSelectedPageNumber('" + _PageNumber.ToString() + "');", true);
    }
    protected void NumResultsPerPageChanged(object sender, EventArgs e)
    {
        _ResultsPerPage = System.Convert.ToInt32(ResultsPerPageDropdown.SelectedValue);

        IEnumerable<SearchResult> co = null;
        if (Request.QueryString["SearchTerms"] != "")
        {
            co = GetSearchResults(Request.QueryString["SearchTerms"]);
        }
        else
        {
            co = GetSearchResults(SearchFederationTextBox.Text);
        }
        if (co != null)
            BindPageNumbers(co.Count());

        ApplySearchResults(co);
    }
}
