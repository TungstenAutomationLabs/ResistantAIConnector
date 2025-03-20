using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using tungstenlabs.integration.resistantai;

namespace ResistantaiMetadataTester
{
  
    public partial class Form1 : Form
    {
        private List<Bitmap> tiffPages = new List<Bitmap>(); // Stores all pages
        private int currentPage = 0; // Current page index

        private DocumentAnalysis DAHelper =new DocumentAnalysis();
        public Form1()
        {
            InitializeComponent();
            lblPageNumber.Width = 500;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            tiffPages.Clear();
            currentPage = 0;
            string docId = DAHelper.GetDocumentWithBoundingBoxes(@"https://eu.id.resistant.ai/oauth2/aus2un1hkrKhPjir4417/v1/token",
                        @"https://api.documents.resistant.ai/v2/submission", @"0oa4oy1pr05JNfPBQ417", @"5sC2ip96HaH-mQhBASjZpXcukqaTSQ8BPg91LatX",
                        @"d943bd2f-e2ce-463f-9715-b29901128ae8", @"https://ktacloudeco-dev.ttaprt.dev.tungstencloud.com/Services/Sdk"
                                , @"D2A967C768C7854B91C210DF77F118A4", "ad90a052-b7d4-4897-83d7-f01e281728cc", "content_hiding");
            //byte[] modifiedTiff= File.ReadAllBytes("C:\\Users\\User\\Desktop\\test.tiff");
            //using (MemoryStream ms = new MemoryStream(modifiedTiff))
            //using (Image image = Image.FromStream(ms))
            //{
            //    tiffPages.Clear();
            //    currentPage = 0;

            //    FrameDimension frameDimension = new FrameDimension(image.FrameDimensionsList[0]);
            //    int totalPages = image.GetFrameCount(frameDimension);

            //    for (int i = 0; i < totalPages; i++)
            //    {
            //        image.SelectActiveFrame(frameDimension, i);
            //        tiffPages.Add(new Bitmap(image));
            //    }

            //    DisplayPage(currentPage);
            //}





            //DisplayPage(currentPage);
            //pictureBox1.Image = apiHelper.GetDocumentWithBoundingBoxes(@"https://eu.id.resistant.ai/oauth2/aus2un1hkrKhPjir4417/v1/token",
            //            @"https://api.documents.resistant.ai/v2/submission", @"0oa4oy1pr05JNfPBQ417", @"5sC2ip96HaH-mQhBASjZpXcukqaTSQ8BPg91LatX",
            //            @"d943bd2f-e2ce-463f-9715-b29901128ae8", @"https://ktacloudeco-dev.ttaprt.dev.tungstencloud.com/Services/Sdk"
            //                    , @"D2A967C768C7854B91C210DF77F118A4", "ad90a052-b7d4-4897-83d7-f01e281728cc");

        }

        private void DisplayPage(int pageIndex)
        {
            if (tiffPages.Count == 0) return;

            pictureBox1.Image = tiffPages[pageIndex]; // Show page in PictureBox
            lblPageNumber.Text = $"Page {pageIndex + 1} of {tiffPages.Count}";
        }

        private void btnNext_Click(object sender, EventArgs e)
        {
            if (currentPage < tiffPages.Count - 1)
            {
                currentPage++;
                DisplayPage(currentPage);
            }
        }

        private void btnPrevious_Click(object sender, EventArgs e)
        {
            if (currentPage > 0)
            {
                currentPage--;
                DisplayPage(currentPage);
            }
        }

        private void pictureBox1_Click(object sender, EventArgs e)
        {

        }
    }
}
