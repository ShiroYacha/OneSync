using Microsoft.Extensions.Logging;
using Microsoft.OneDrive.Sdk;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Microsoft.Data.Entity;
using Microsoft.Data.Entity.Infrastructure;
using System.Threading.Tasks;
using GalaSoft.MvvmLight.Ioc;
using Windows.Security.Cryptography.Core;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace OneSync
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private Synchronizer synchronizer;

        public MainPage()
        {
            this.InitializeComponent();

            synchronizer = SimpleIoc.Default.GetInstance<Synchronizer>();
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            // initialize synchronizer
            await synchronizer.Setup();

            // db context stuff
            List<Blog> blogs;
            using (var db = new BloggingContext())
            {
                db.Database.EnsureCreated();

                blogs = db.Blogs.ToList();
                Blogs.ItemsSource = blogs;
            }

        }

        private int HashStringToIntId(string text)
        {
            // Create a string that contains the name of the hashing algorithm to use.
            String strAlgName = HashAlgorithmNames.Md5;

            // Create a HashAlgorithmProvider object.
            HashAlgorithmProvider objAlgProv = HashAlgorithmProvider.OpenAlgorithm(strAlgName);

            // Create a CryptographicHash object. This object can be reused to continually
            // hash new messages.
            CryptographicHash objHash = objAlgProv.CreateHash();

            // Hash message 1.
            IBuffer buffMsg = CryptographicBuffer.ConvertStringToBinary(text, BinaryStringEncoding.Utf8);
            objHash.Append(buffMsg);
            IBuffer buffHash = objHash.GetValueAndReset();
            return Convert.ToInt32(buffHash.GetByte(0));
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            synchronizer.LoggingMode = true;
            using (var db = new BloggingContext())
            {
                var blog = new Blog { BlogId = HashStringToIntId(NewBlogUrl.Text), Url=NewBlogUrl.Text };
                db.Blogs.Add(blog);
                db.SaveChanges();

                Blogs.ItemsSource = db.Blogs.ToList();
            }
            synchronizer.LoggingMode = false;
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            synchronizer.LoggingMode = true;
            using (var db = new BloggingContext())
            {
                db.Blogs.RemoveRange(Blogs.SelectedItems.Cast<Blog>());
                db.SaveChanges();

                Blogs.ItemsSource = db.Blogs.ToList();
            }
            synchronizer.LoggingMode = false;
        }

        private async void Upload_Click(object sender, RoutedEventArgs e)
        {
            var synchronizer = SimpleIoc.Default.GetInstance<Synchronizer>();
            await synchronizer.UpdateOnedrive();
        }

        private async void Download_Click(object sender, RoutedEventArgs e)
        {
            var synchronizer = SimpleIoc.Default.GetInstance<Synchronizer>();
            using (var db = new BloggingContext())
            {
                // update the database from remote
                await synchronizer.Download(db);

                // invalidate
                Blogs.ItemsSource = db.Blogs.ToList();
            }

        }

        private async void Initialize_Click(object sender, RoutedEventArgs e)
        {
            var synchronizer = SimpleIoc.Default.GetInstance<Synchronizer>();
            using (var db = new BloggingContext())
            {                
                // clear local db
                await synchronizer.InitializeDb(db);

                // invalidate
                Blogs.ItemsSource = db.Blogs.ToList();
            }

        }

        private async void InitializeAll_Click(object sender, RoutedEventArgs e)
        {
            var synchronizer = SimpleIoc.Default.GetInstance<Synchronizer>();
            using (var db = new BloggingContext())
            {
                // clear onedrive
                await synchronizer.InitializeOnedrive();

                // clear local db
                await synchronizer.InitializeDb(db);

                // invalidate
                Blogs.ItemsSource = db.Blogs.ToList();
            }

        }
    }
}
