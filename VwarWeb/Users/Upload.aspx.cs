﻿using System;
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
using System.IO;
using System.Diagnostics;
using System.Threading;
using Ionic.Zip;
using vwarDAL;
using Utils;
using System.ComponentModel;
using System.Collections.Generic;

public class FedoraFileInfo
{
    public string SourceFilepath;
    public string DestinationFilename;
    protected bool mNeedsId;
    public bool NeedsId
    {
        get { return mNeedsId; }
    }
    public FedoraFileInfo()
    {
        mNeedsId = false;
    }
}

public class FedoraReferencedFileInfo : FedoraFileInfo
{
    public enum ReferencedIdType
    {
        DISPLAY_FILE,
        SCREENSHOT,
        DEV_LOGO,
        SPONSOR_LOGO
    }

    public ReferencedIdType idType;
    public FedoraReferencedFileInfo()
    {
        mNeedsId = true;
    }
}

public class FedoraFileUploadCollection
{
    public System.Web.SessionState.HttpSessionState session;
    public ContentObject currentFedoraObject;
    private List<FedoraFileInfo> mFileList;
    public List<FedoraFileInfo> FileList
    {
        get { return mFileList;}
    }

    public FedoraFileUploadCollection()
    {  
        mFileList = new List<FedoraFileInfo>();
    }
}


/// <summary>
/// Web Page that allows for the uploading of 3D model content and associated metadata
/// </summary>
public partial class Users_Upload : Website.Pages.PageBase
{
    protected void Page_Load(object sender, EventArgs e)
    {
        if (this.Page.Master.FindControl("SearchPanel") != null)
        {
            //hide the search panel
            this.Page.Master.FindControl("SearchPanel").Visible = false;
            
        }

        if (!Page.IsPostBack)
        {
            HttpContext.Current.Session["fileStatus"] = null; //Reset the FileStatus in case page was refreshed
        }
    }



    /// <summary>
    /// AJAX-enabled web method to detect the format of the file
    /// </summary>
    /// <param name="filename">The uploaded filename </param>
    /// <returns>A JSON-encoded FileStatus object containing the extension and the type (Recognized, Unrecognized, or Viewable)</returns>
    [System.Web.Services.WebMethod()]
    [System.Web.Script.Services.ScriptMethod(ResponseFormat = System.Web.Script.Services.ResponseFormat.Json)]
    public static FileStatus DetectFormat(string filename)
    {

        FileStatus currentStatus = new FileStatus("", FormatType.UNRECOGNIZED);


        //The temp filename (hashname) is a sha1 hash plus a random number   
        currentStatus.hashname = filename; 
        currentStatus.msg = FileStatus.UnrecognizedMessage;

        //Check to see if it's an skp file
        if (string.Compare(currentStatus.hashname.Substring(currentStatus.hashname.LastIndexOf('.')), ".skp", true) == 0)
        {
            currentStatus.type = FormatType.VIEWABLE;
            currentStatus.extension = ".skp";
        }
        else
        {
            int recognizedCount = 0;
            int viewableCount = 0;
            try
            {
                using (ZipFile zip = ZipFile.Read(HttpContext.Current.Server.MapPath("~/App_Data/") + currentStatus.hashname))
                {
                    int i = 0;
                    foreach (string s in zip.EntryFileNames)
                    {
                        
                        System.IO.FileInfo f = new System.IO.FileInfo(s);
                        if (FileStatus.GetType(f.Extension) == FormatType.VIEWABLE)
                        {

                                if (zip.Entries[i].UncompressedSize == 0)
                                {
                                    currentStatus.msg = FileStatus.ModelFileEmptyMessage;
                                    return currentStatus;
                                }
                          
                            currentStatus.extension = f.Extension;
                            currentStatus.type = FormatType.VIEWABLE;

                            recognizedCount++;
                        }
                        else if (FileStatus.GetType(f.Extension) == FormatType.RECOGNIZED)
                        {
                            if (currentStatus.type != FormatType.VIEWABLE)
                            {
                                if (zip.Entries[i].UncompressedSize == 0)
                                {
                                    currentStatus.msg = FileStatus.ModelFileEmptyMessage;
                                    return currentStatus;
                                }
                                currentStatus.extension = f.Extension;
                                currentStatus.type = FormatType.RECOGNIZED;
                            }
                            currentStatus.msg = FileStatus.WarningMessage;
                            viewableCount++;
                        }
                        i++;
                    }
                }
            }
            catch (ZipException e)
            {
                currentStatus.msg = FileStatus.InvalidZipMessage;
                return currentStatus;
            }
            //Make sure there is only one recognized or viewable model format in the zip file
            //If multiple have been detected, set the format type and break
            if (viewableCount > 1)
            {
                currentStatus.type = FormatType.MULTIPLE_RECOGNIZED;
                currentStatus.msg = FileStatus.MultipleRecognizedMessage;

            }
        }
        if (currentStatus.type == FormatType.UNRECOGNIZED ||
            currentStatus.type == FormatType.MULTIPLE_RECOGNIZED)
        {
            deleteTempFile(currentStatus.hashname);
        }
        else
        {
            HttpContext.Current.Session["fileStatus"] = currentStatus;
        }

        ContentObject tempFedoraObject = new ContentObject(null);
        tempFedoraObject.UploadedDate = DateTime.Now;
        tempFedoraObject.LastModified = DateTime.Now;
        tempFedoraObject.Views = 0;
        tempFedoraObject.SubmitterEmail = HttpContext.Current.User.Identity.Name.Trim();
        HttpContext.Current.Session["contentObject"] = tempFedoraObject;

        return currentStatus;
    }



    /// <summary>
    /// Sends the uploaded file through the conversion process and stores the temporary files in the App_Data folder. 
    /// Also updates the temporary content object and FileStatus for the session.
    /// </summary>
    /// <returns>A JSON-encoded FileStatus object containing the extension, the type (Recognized, Unrecognized, or Viewable), and conversion status</returns>
    [System.Web.Services.WebMethod()]
    [System.Web.Script.Services.ScriptMethod(ResponseFormat = System.Web.Script.Services.ResponseFormat.Json)]
    public static FileStatus Convert()
    {

        Utility_3D.Model_Packager pack = new Utility_3D.Model_Packager();
        Utility_3D _3d = new Utility_3D();
        _3d.Initialize(Website.Config.ConversionLibarayLocation);

        Utility_3D.ConvertedModel model = null;
        
        FileStatus status = (FileStatus)HttpContext.Current.Session["fileStatus"];
        using (FileStream stream = new FileStream(HttpContext.Current.Server.MapPath("~/App_data/" + status.hashname), FileMode.Open))
        {

            ContentObject tempFedoraObject = (ContentObject)HttpContext.Current.Session["contentObject"];
            try //convert the model
            {

                model = pack.Convert(stream, status.hashname);

                if (model._ModelData.VertexCount.Polys == 0 && model._ModelData.VertexCount.Verts == 0)
                {
                    //don't say it's ok!
                }

                status.converted = "true";
                HttpContext.Current.Session["fileStatus"] = status;

                Utility_3D.Parser.ModelData mdata = model._ModelData;
                tempFedoraObject.NumPolygons = mdata.VertexCount.Polys;
                tempFedoraObject.NumTextures = mdata.ReferencedTextures.Length;
                tempFedoraObject.UpAxis = mdata.TransformProperties.UpAxis;
                tempFedoraObject.UnitScale = System.Convert.ToString(mdata.TransformProperties.UnitMeters);

                HttpContext.Current.Session["contentObject"] = tempFedoraObject;


                //Save the O3D file for the viewer into a temporary directory
                var tempfile = HttpContext.Current.Server.MapPath("~/App_Data/viewerTemp/" + status.hashname).Replace(".skp", ".zip");
                using (System.IO.FileStream savefile = new FileStream(tempfile, FileMode.Create))
                {
                    byte[] filedata = new Byte[model.data.Length];
                    model.data.CopyTo(filedata, 0);
                    savefile.Write(model.data, 0, (int)model.data.Length);
                }
                ConvertFileToO3D(HttpContext.Current, tempfile);
                var rootDir = HttpContext.Current.Server.MapPath("~/App_Data/converterTemp/");
                var fileName = Path.Combine(rootDir,status.hashname.Replace(".skp", ".zip"));
                if (!Directory.Exists(rootDir))
                {
                    Directory.CreateDirectory(rootDir);
                }
                if (File.Exists( fileName))
                {
                    File.Delete(fileName);
                }
                File.Move(tempfile, fileName);
            }
            catch (Exception e) //Error while converting
            {
                stream.Close();
                //FileStatus.converted is set to false by default, no need to set
                status.msg = FileStatus.ConversionFailedMessage; //Add the conversion failed message
                deleteTempFile(status.hashname);
                HttpContext.Current.Session["fileStatus"] = null; //Reset the FileStatus for another upload attempt
                
            }
        }
        return status;
    }

    static void UploadToFedora(object data)
    {
        var factory = new DataAccessFactory();
        IDataRepository dal = factory.CreateDataRepositorProxy();
        FedoraFileUploadCollection modelsCol = data as FedoraFileUploadCollection;
        if (modelsCol == null) return;
        string pid = modelsCol.currentFedoraObject.PID;
        

        foreach (FedoraFileInfo f in modelsCol.FileList)
        {
            string newId;
            using (FileStream fstream = File.OpenRead(f.SourceFilepath))
            {
                newId = dal.SetContentFile(fstream, pid, f.DestinationFilename);
            }

            if (f.NeedsId)
            {
                switch (((FedoraReferencedFileInfo)f).idType)
                {
                    case FedoraReferencedFileInfo.ReferencedIdType.DISPLAY_FILE:
                        modelsCol.currentFedoraObject.DisplayFileId = newId;
                        break;
                    case FedoraReferencedFileInfo.ReferencedIdType.SCREENSHOT:
                        modelsCol.currentFedoraObject.ScreenShotId = newId;
                        break;
                    case FedoraReferencedFileInfo.ReferencedIdType.DEV_LOGO:
                        modelsCol.currentFedoraObject.DeveloperLogoImageFileNameId = newId;
                        break;
                    case FedoraReferencedFileInfo.ReferencedIdType.SPONSOR_LOGO:
                        modelsCol.currentFedoraObject.SponsorLogoImageFileNameId = newId;
                        break;
                    default: break;
                }
            }
        }
        modelsCol.currentFedoraObject.Ready = true;
        dal.UpdateContentObject(modelsCol.currentFedoraObject);        
    }


    /// <summary>
    /// Clears the Session variables and stored temp files from the server
    /// </summary>
    /// <param name="filename">The name of the temporary file (possibly "undefined") to clean up, if necessary</param>
    [System.Web.Services.WebMethod()]
    [System.Web.Script.Services.ScriptMethod(ResponseFormat = System.Web.Script.Services.ResponseFormat.Json)]
    public static void UploadReset(string filename)
    {    
        string basePath = HttpContext.Current.Server.MapPath("~/App_Data/");
        //Delete the temp file if it exists
        if (File.Exists(HttpContext.Current.Server.MapPath("~/App_Data/" + filename)))
        {
            deleteTempFile(filename);          

            string converterTempPath = basePath + "converterTemp/" + filename;
            if (File.Exists(converterTempPath))
            {
                try
                {
                    File.Delete(converterTempPath);
                } catch {}
            }

            string viewerTempPath = basePath + "viewerTemp/" + filename.Replace("zip", "o3d").Replace("skp", "o3d").ToLower() ;
            if (File.Exists(viewerTempPath))
            {
                try
                {
                    File.Delete(viewerTempPath);
                }
                catch { }
            }
        }

        

        //Delete the FileStatus from session if it exists
        if(HttpContext.Current.Session["fileStatus"] != null)
        {
            HttpContext.Current.Session["fileStatus"] = null;
        }

         //Delete the model from session if it exists
        if(HttpContext.Current.Session["contentObject"] != null)
        {
            HttpContext.Current.Session["contentObject"] = null;
        }

        if (filename != "" && filename != "undefined")
        {
            string imagePath = HttpContext.Current.Server.MapPath("~/App_Data/imageTemp/");
            string basehash = filename.Substring(0, filename.LastIndexOf(".") - 1);
            foreach (string imgFileName in Directory.GetFiles(imagePath, "*" + basehash + "*"))
            {
                File.Delete(imgFileName);
            }
        }
        
    }


    /// <summary>
    /// Updates the content object with the metadata provided by the user in Step 1.
    /// </summary>
    /// <param name="TitleInput">The text from the "Title" text field (NewUpload.ascx)</param>
    /// <param name="DescriptionInput">The text from the "Description" textarea (NewUpload.ascx)</param>
    /// <param name="TagsInput">TagsInput - The comma or space-delimited list of tags from the tags text field (NewUpload.ascx)</param>
    /// <returns>A JSON object containing the parameters for the ViewerLoader javascript object constructor</returns>
    [System.Web.Services.WebMethod()]
    [System.Web.Script.Services.ScriptMethod(ResponseFormat = System.Web.Script.Services.ResponseFormat.Json)]
    public static JsonWrappers.ViewerLoadParams Step1_Submit(string TitleInput, string DescriptionInput, string TagsInput)
    {
        FileStatus currentStatus = (FileStatus)HttpContext.Current.Session["fileStatus"];
        var fileName = TitleInput.Trim().Replace(' ', '_') ;
        
        if(fileName.LastIndexOf(".skp") != -1)
        {
            fileName += ".skp";
        }
        else
        {
            fileName += ".zip";
        }
        if (currentStatus != null)
        {

            currentStatus.filename = fileName;
        }

        ContentObject tempFedoraCO = (ContentObject)HttpContext.Current.Session["contentObject"];
        tempFedoraCO.PID = "";
        tempFedoraCO.Title = TitleInput.Trim();
        tempFedoraCO.Description = DescriptionInput.Trim();
        tempFedoraCO.Location = fileName.Replace(".skp", ".zip");

        


        string cleanTags = "";
        foreach (string s in TagsInput.Split(','))
        {
            cleanTags += s.Trim() + ",";
        }
        cleanTags = cleanTags.Trim(',');

        tempFedoraCO.Keywords = cleanTags;



        JsonWrappers.ViewerLoadParams jsReturnParams = new JsonWrappers.ViewerLoadParams();
        jsReturnParams.FlashLocation = tempFedoraCO.Location;
        FedoraFileUploadCollection modelsCollection = new FedoraFileUploadCollection();
        if (currentStatus.type == FormatType.VIEWABLE)
        {
            tempFedoraCO.DisplayFile = currentStatus.filename.Replace("zip", "o3d").Replace("skp", "o3d");
            jsReturnParams.IsViewable = true;
            jsReturnParams.BasePath = "../Public/";
            jsReturnParams.BaseContentUrl = "Model.ashx?temp=true&file=";
            jsReturnParams.O3DLocation = currentStatus.hashname.Replace("zip", "o3d").Replace("skp", "o3d");
            jsReturnParams.FlashLocation = currentStatus.hashname;
            jsReturnParams.ShowScreenshot = true;
            jsReturnParams.UpAxis = tempFedoraCO.UpAxis;
            jsReturnParams.UnitScale = tempFedoraCO.UnitScale;

            FedoraReferencedFileInfo displayFileInfo = new FedoraReferencedFileInfo();
            displayFileInfo.idType = FedoraReferencedFileInfo.ReferencedIdType.DISPLAY_FILE;
            displayFileInfo.SourceFilepath = HttpContext.Current.Server.MapPath("~/App_Data/viewerTemp/" + jsReturnParams.O3DLocation);
            displayFileInfo.DestinationFilename = tempFedoraCO.DisplayFile;
            modelsCollection.FileList.Add(displayFileInfo);

            FedoraFileInfo originalFileInfo = new FedoraFileInfo();
            originalFileInfo.SourceFilepath = HttpContext.Current.Server.MapPath("~/App_Data/" + currentStatus.hashname);
            originalFileInfo.DestinationFilename = "original_" + currentStatus.filename;
            modelsCollection.FileList.Add(originalFileInfo);

            FedoraFileInfo convertedFileInfo = new FedoraFileInfo();
            convertedFileInfo.SourceFilepath = HttpContext.Current.Server.MapPath("~/App_Data/converterTemp/" + currentStatus.hashname.Replace(".skp", ".zip"));
            convertedFileInfo.DestinationFilename = tempFedoraCO.Location;
            modelsCollection.FileList.Add(convertedFileInfo);
            
        }
        else if (currentStatus.type == FormatType.RECOGNIZED)
        {
            tempFedoraCO.DisplayFile = "N/A";
            FedoraFileInfo originalFileInfo = new FedoraFileInfo();
            originalFileInfo.SourceFilepath = HttpContext.Current.Server.MapPath("~/App_Data/" + currentStatus.hashname);
            originalFileInfo.DestinationFilename = currentStatus.filename;
            modelsCollection.FileList.Add(originalFileInfo);
        }

        var factory = new DataAccessFactory();
        IDataRepository dal = factory.CreateDataRepositorProxy();
        dal.InsertContentObject(tempFedoraCO);
        currentStatus.pid = tempFedoraCO.PID;
        modelsCollection.currentFedoraObject = tempFedoraCO;


        WaitCallback doFedoraUpload = new WaitCallback(UploadToFedora);
        ThreadPool.QueueUserWorkItem(doFedoraUpload, modelsCollection);
        /*Thread obj = new Thread(new ParameterizedThreadStart(UploadToFedora));
        
         obj.IsBackground = true;
         obj.Priority = ThreadPriority.Highest;
         obj.Start(modelsCollection);*/
       // BackgroundWorker worker = new BackgroundWorker();
       // worker.DoWork += new DoWorkEventHandler(UploadToFedora);
       // worker.RunWorkerAsync(modelsCollection);
        return jsReturnParams;

        
    }


    /// <summary>
    /// Updates the temporary content object with the Up Axis and Unit Scale set in Step 2.
    /// </summary>
    /// <param name="ScaleValue">A float value representing the scale, expressed in meters (NewUpload.ascx)</param>
    /// <param name="UpAxis">A string, either "Y" or "Z", that specifies the selected Up Axis value (NewUpload.ascx)</param>
    /// <returns>A pipe-delimited string array that has the info to be filled in</returns>
    [System.Web.Services.WebMethod()]
    [System.Web.Script.Services.ScriptMethod()]
    public static JsonWrappers.UploadDetailDefaults Step2_Submit(string ScaleValue, string UpAxis)
    {
        HttpContext context = HttpContext.Current;
        FileStatus currentStatus = (FileStatus) context.Session["fileStatus"];

        var factory = new DataAccessFactory();
        IDataRepository dal = factory.CreateDataRepositorProxy();
        ContentObject tempCO = dal.GetContentObjectById(currentStatus.pid, false);
        tempCO.UpAxis = UpAxis;
        tempCO.UnitScale = ScaleValue;
        //dal.UpdateContentObject(tempCO);
        context.Session["contentObject"] = tempCO;


        //Bind the 
        JsonWrappers.UploadDetailDefaults jsReturnParams = new JsonWrappers.UploadDetailDefaults();
        if (HttpContext.Current.User.Identity.IsAuthenticated)
        {
            UserProfile p = null;
            try
            {
                p = UserProfileDB.GetUserProfileByUserName(context.User.Identity.Name);
            }
            catch { }

            if (p != null)
            {
                jsReturnParams.HasDefaults = true;
                jsReturnParams.DeveloperName = p.DeveloperName;
                jsReturnParams.ArtistName = p.ArtistName;
                jsReturnParams.DeveloperUrl = p.WebsiteURL;
                jsReturnParams.SponsorName = p.SponsorName;

                string tempImagePath = context.Server.MapPath("~/App_Data/imageTemp/");
                if (p.DeveloperLogo != null)
                {

                    string extension = p.DeveloperLogoContentType.Substring(p.DeveloperLogoContentType.LastIndexOf("/") + 1);
                    string tempDevLogoFilename = "devlogo_" + currentStatus.hashname.Replace("zip", extension);
                    using (FileStream stream = new FileStream(tempImagePath + tempDevLogoFilename, FileMode.Create))
                    {
                        stream.Write(p.DeveloperLogo, 0, p.DeveloperLogo.Length);
                    }

                    jsReturnParams.DeveloperLogoFilename = tempDevLogoFilename;
                }

                if (p.SponsorLogo != null)
                {
                    string extension = p.SponsorLogoContentType.Substring(p.SponsorLogoContentType.LastIndexOf("/") + 1);
                    string tempSponsorLogoFilename = "sponsorlogo_" + currentStatus.hashname.Replace("zip", extension);
                    using (FileStream stream = new FileStream(tempImagePath + tempSponsorLogoFilename, FileMode.Create))
                    {
                        stream.Write(p.SponsorLogo, 0, p.SponsorLogo.Length);
                    }

                    jsReturnParams.SponsorLogoFilename = tempSponsorLogoFilename;
                }
            }
        }

        return jsReturnParams;
    }


    /// <summary>
    /// Binds the details from step 3 to the content object, sends it to Fedora, then adds the model and image datastreams.
    /// </summary>
    /// <param name="DeveloperName">The text from the "Developer Name" text field (NewUpload.ascx)</param>
    /// <param name="ArtistName">The text from the "Artist Name" text field (NewUpload.ascx)</param>
    /// <param name="DeveloperUrl">The url from the "Developer Url" text field (NewUpload.ascx)</param>
    /// <param name="SponsorName">The text from the "Sponsor Name" text field (NewUpload.ascx)</param>
    /// <param name="SponsorUrl">The url from the sponsor url text field (NewUpload.ascx)</param>
    /// <param name="LicenseType"> The shorthand notation for the Creative Commons License type</param>
    /// <returns>A string containing the ContentObjectID for the newly inserted Content Object</returns>
    [System.Web.Services.WebMethod()]
    [System.Web.Script.Services.ScriptMethod()]
    public static string SubmitUpload(string DeveloperName, string ArtistName, string DeveloperUrl, string SponsorName, string SponsorUrl, string LicenseType, string AgreementVerified)
    {

        if (AgreementVerified != "true")
        {
            return "unverified";
        }

        try
        {
            FileStatus status = (FileStatus)HttpContext.Current.Session["fileStatus"];
            var factory = new DataAccessFactory();
            IDataRepository dal = factory.CreateDataRepositorProxy();
            ContentObject tempCO = dal.GetContentObjectById(status.pid, false);
            while (!tempCO.Ready)
            {
                Thread.Sleep(250);
                tempCO = dal.GetContentObjectById(status.pid, false);
            }
            tempCO.DeveloperName = DeveloperName;
            tempCO.ArtistName = ArtistName;
            tempCO.MoreInformationURL = DeveloperUrl;
            //tempCO.SponsorURL = SponsorUrl; !missing SponsorUrl metadata in ContentObject
            if (LicenseType == "publicdomain")
            {
                tempCO.CreativeCommonsLicenseURL = "http://creativecommons.org/publicdomain/mark/1.0/";
            }
            else
            {
                tempCO.CreativeCommonsLicenseURL = String.Format(System.Configuration.ConfigurationManager.AppSettings["CCBaseUrl"], LicenseType);
            }
            tempCO.SponsorName = SponsorName;

            //Upload the thumbnail and logos
            string filename = status.hashname;
            string basehash = filename.Substring(0, filename.LastIndexOf(".") - 1);
            foreach (FileInfo f in new DirectoryInfo(HttpContext.Current.Server.MapPath("~/App_Data/imageTemp")).GetFiles("*" + basehash + "*"))
            {
                using (FileStream fstream = f.OpenRead())
                {
                    string type = f.Name.Substring(0, f.Name.IndexOf('_'));
                    switch (type)
                    {
                        case ImagePrefix.DEVELOPER_LOGO:
                            tempCO.DeveloperLogoImageFileName = "developer_logo" + f.Extension;
                            tempCO.DeveloperLogoImageFileNameId = dal.SetContentFile(fstream, tempCO, tempCO.DeveloperLogoImageFileName);
                            break;

                        case ImagePrefix.SPONSOR_LOGO:
                            tempCO.SponsorLogoImageFileName = "sponsor_logo" + f.Extension;
                            tempCO.SponsorLogoImageFileNameId = dal.SetContentFile(fstream, tempCO, tempCO.SponsorLogoImageFileName);
                            break;

                        case ImagePrefix.SCREENSHOT:
                            tempCO.ScreenShot = "screenshot" + f.Extension;
                            tempCO.ScreenShotId = dal.SetContentFile(fstream, tempCO, tempCO.ScreenShot);
                            break;

                        default:
                            break;
                    }
                }
            }
            tempCO.UnitScale = ((ContentObject)HttpContext.Current.Session["contentObject"]).UnitScale;
            tempCO.UpAxis = ((ContentObject)HttpContext.Current.Session["contentObject"]).UpAxis;
            tempCO.Enabled = true;
            dal.UpdateContentObject(tempCO);
            UploadReset(filename);
            return tempCO.PID;
        }
        catch(Exception e) {
            //add fail logic here
            return "fedoraError";
        }
    }


    /// <summary>
    /// Deletes an image file from the imageTemp directory, resulting from a re-upload of an image file.
    /// </summary>
    /// <param name="filename">the name of the file (no path) in imageTemp that needs to be deleted</param>
    [System.Web.Services.WebMethod()]
    [System.Web.Script.Services.ScriptMethod(ResponseFormat = System.Web.Script.Services.ResponseFormat.Json)]
    public static void DeleteImage(string filename)
    {
        string path = HttpContext.Current.Server.MapPath("~/App_Data/imageTemp/" + filename);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }


    /// <summary>
    /// Deletes a file from the base temporary directory.
    /// </summary>
    /// <param name="filename">The name of the file to be deleted (no path)</param>
    public static void deleteTempFile(string filename)
    {
        File.Delete(HttpContext.Current.Server.MapPath("~/App_Data/" + filename));
    }


    

    /// <summary>
    /// Converts a file from its native format to the O3D format.
    /// </summary>
    /// <param name="context">The current web context.</param>
    /// <param name="path"></param>
    /// <returns></returns>
    private static string ConvertFileToO3D(HttpContext context, string path)
    {
        HttpRequest request = context.Request;

        var application = context.Server.MapPath("~/processes/o3dConverter.exe");//Path.Combine(Path.Combine(request.PhysicalApplicationPath, "bin"), "o3dConverter.exe");
        System.Diagnostics.ProcessStartInfo processInfo = new System.Diagnostics.ProcessStartInfo(application);
        processInfo.Arguments = String.Format("\"{0}\" \"{1}\"", path, path.Replace("zip", "o3d").Replace("skp", "o3d"));
        processInfo.WindowStyle = ProcessWindowStyle.Hidden;
        processInfo.RedirectStandardError = true;
        processInfo.CreateNoWindow = true;
        processInfo.UseShellExecute = false;
        var p = Process.Start(processInfo);
        var error = p.StandardError.ReadToEnd();
        return path.Replace("zip", "o3d");
    }
    

}
