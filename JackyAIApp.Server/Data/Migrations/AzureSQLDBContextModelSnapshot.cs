﻿// <auto-generated />
using System;
using JackyAIApp.Server.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace JackyAIApp.Server.Data.Migrations
{
    [DbContext(typeof(AzureSQLDBContext))]
    partial class AzureSQLDBContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "8.0.17")
                .HasAnnotation("Relational:MaxIdentifierLength", 128);

            SqlServerModelBuilderExtensions.UseIdentityColumns(modelBuilder);

            modelBuilder.Entity("JackyAIApp.Server.Data.Models.SQL.ClozeTest", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("nvarchar(450)");

                    b.Property<string>("Answer")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Question")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("WordId")
                        .IsRequired()
                        .HasColumnType("nvarchar(450)");

                    b.HasKey("Id");

                    b.HasIndex("WordId");

                    b.ToTable("ClozeTests");
                });

            modelBuilder.Entity("JackyAIApp.Server.Data.Models.SQL.ClozeTestOption", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("nvarchar(450)");

                    b.Property<string>("ClozeTestId")
                        .IsRequired()
                        .HasColumnType("nvarchar(450)");

                    b.Property<string>("OptionText")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.HasKey("Id");

                    b.HasIndex("ClozeTestId");

                    b.ToTable("ClozeTestOptions");
                });

            modelBuilder.Entity("JackyAIApp.Server.Data.Models.SQL.Definition", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("nvarchar(450)");

                    b.Property<string>("Chinese")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("English")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("WordMeaningId")
                        .IsRequired()
                        .HasColumnType("nvarchar(450)");

                    b.HasKey("Id");

                    b.HasIndex("WordMeaningId");

                    b.ToTable("Definitions");
                });

            modelBuilder.Entity("JackyAIApp.Server.Data.Models.SQL.ExampleSentence", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("nvarchar(450)");

                    b.Property<string>("Chinese")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("English")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("WordMeaningId")
                        .IsRequired()
                        .HasColumnType("nvarchar(450)");

                    b.HasKey("Id");

                    b.HasIndex("WordMeaningId");

                    b.ToTable("ExampleSentences");
                });

            modelBuilder.Entity("JackyAIApp.Server.Data.Models.SQL.JiraConfig", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("nvarchar(450)");

                    b.Property<string>("Domain")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Email")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Token")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("UserId")
                        .IsRequired()
                        .HasColumnType("nvarchar(450)");

                    b.HasKey("Id");

                    b.HasIndex("UserId");

                    b.ToTable("JiraConfigs");
                });

            modelBuilder.Entity("JackyAIApp.Server.Data.Models.SQL.SentenceTest", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("nvarchar(450)");

                    b.Property<string>("Context")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<int>("DifficultyLevel")
                        .HasColumnType("int");

                    b.Property<string>("GrammarPattern")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Prompt")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("SampleAnswer")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("WordId")
                        .IsRequired()
                        .HasColumnType("nvarchar(450)");

                    b.HasKey("Id");

                    b.HasIndex("WordId");

                    b.ToTable("SentenceTests");
                });

            modelBuilder.Entity("JackyAIApp.Server.Data.Models.SQL.TranslationTest", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("nvarchar(450)");

                    b.Property<string>("Chinese")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("English")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("WordId")
                        .IsRequired()
                        .HasColumnType("nvarchar(450)");

                    b.HasKey("Id");

                    b.HasIndex("WordId");

                    b.ToTable("TranslationTests");
                });

            modelBuilder.Entity("JackyAIApp.Server.Data.Models.SQL.User", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("nvarchar(450)");

                    b.Property<decimal>("CreditBalance")
                        .HasColumnType("decimal(20,0)");

                    b.Property<string>("Email")
                        .HasColumnType("nvarchar(max)");

                    b.Property<bool?>("IsAdmin")
                        .HasColumnType("bit");

                    b.Property<DateTime>("LastUpdated")
                        .HasColumnType("datetime2");

                    b.Property<string>("Name")
                        .HasColumnType("nvarchar(max)");

                    b.Property<decimal>("TotalCreditsUsed")
                        .HasColumnType("decimal(20,0)");

                    b.HasKey("Id");

                    b.ToTable("Users");
                });

            modelBuilder.Entity("JackyAIApp.Server.Data.Models.SQL.UserWord", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("int");

                    SqlServerPropertyBuilderExtensions.UseIdentityColumn(b.Property<int>("Id"));

                    b.Property<DateTime>("DateAdded")
                        .HasColumnType("datetime2");

                    b.Property<string>("UserId")
                        .IsRequired()
                        .HasColumnType("nvarchar(450)");

                    b.Property<string>("WordId")
                        .IsRequired()
                        .HasColumnType("nvarchar(450)");

                    b.HasKey("Id");

                    b.HasIndex("UserId");

                    b.HasIndex("WordId");

                    b.ToTable("UserWords");
                });

            modelBuilder.Entity("JackyAIApp.Server.Data.Models.SQL.Word", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("nvarchar(450)");

                    b.Property<bool?>("DataInvalid")
                        .HasColumnType("bit");

                    b.Property<DateTime>("DateAdded")
                        .HasColumnType("datetime2");

                    b.Property<string>("KKPhonics")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<DateTime>("LastUpdated")
                        .HasColumnType("datetime2");

                    b.Property<string>("WordText")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.HasKey("Id");

                    b.ToTable("Words");
                });

            modelBuilder.Entity("JackyAIApp.Server.Data.Models.SQL.WordMeaning", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("nvarchar(450)");

                    b.Property<string>("PartOfSpeech")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("WordId")
                        .IsRequired()
                        .HasColumnType("nvarchar(450)");

                    b.HasKey("Id");

                    b.HasIndex("WordId");

                    b.ToTable("WordMeanings");
                });

            modelBuilder.Entity("JackyAIApp.Server.Data.Models.SQL.WordMeaningTag", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("nvarchar(450)");

                    b.Property<string>("TagType")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Word")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("WordMeaningId")
                        .IsRequired()
                        .HasColumnType("nvarchar(450)");

                    b.HasKey("Id");

                    b.HasIndex("WordMeaningId");

                    b.ToTable("WordMeaningTags");
                });

            modelBuilder.Entity("JackyAIApp.Server.Data.Models.SQL.ClozeTest", b =>
                {
                    b.HasOne("JackyAIApp.Server.Data.Models.SQL.Word", "Word")
                        .WithMany("ClozeTests")
                        .HasForeignKey("WordId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Word");
                });

            modelBuilder.Entity("JackyAIApp.Server.Data.Models.SQL.ClozeTestOption", b =>
                {
                    b.HasOne("JackyAIApp.Server.Data.Models.SQL.ClozeTest", "ClozeTest")
                        .WithMany("Options")
                        .HasForeignKey("ClozeTestId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("ClozeTest");
                });

            modelBuilder.Entity("JackyAIApp.Server.Data.Models.SQL.Definition", b =>
                {
                    b.HasOne("JackyAIApp.Server.Data.Models.SQL.WordMeaning", "WordMeaning")
                        .WithMany("Definitions")
                        .HasForeignKey("WordMeaningId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("WordMeaning");
                });

            modelBuilder.Entity("JackyAIApp.Server.Data.Models.SQL.ExampleSentence", b =>
                {
                    b.HasOne("JackyAIApp.Server.Data.Models.SQL.WordMeaning", "WordMeaning")
                        .WithMany("ExampleSentences")
                        .HasForeignKey("WordMeaningId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("WordMeaning");
                });

            modelBuilder.Entity("JackyAIApp.Server.Data.Models.SQL.JiraConfig", b =>
                {
                    b.HasOne("JackyAIApp.Server.Data.Models.SQL.User", "User")
                        .WithMany("JiraConfigs")
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("User");
                });

            modelBuilder.Entity("JackyAIApp.Server.Data.Models.SQL.SentenceTest", b =>
                {
                    b.HasOne("JackyAIApp.Server.Data.Models.SQL.Word", "Word")
                        .WithMany("SentenceTests")
                        .HasForeignKey("WordId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Word");
                });

            modelBuilder.Entity("JackyAIApp.Server.Data.Models.SQL.TranslationTest", b =>
                {
                    b.HasOne("JackyAIApp.Server.Data.Models.SQL.Word", "Word")
                        .WithMany("TranslationTests")
                        .HasForeignKey("WordId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Word");
                });

            modelBuilder.Entity("JackyAIApp.Server.Data.Models.SQL.UserWord", b =>
                {
                    b.HasOne("JackyAIApp.Server.Data.Models.SQL.User", "User")
                        .WithMany("UserWords")
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("JackyAIApp.Server.Data.Models.SQL.Word", "Word")
                        .WithMany()
                        .HasForeignKey("WordId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("User");

                    b.Navigation("Word");
                });

            modelBuilder.Entity("JackyAIApp.Server.Data.Models.SQL.WordMeaning", b =>
                {
                    b.HasOne("JackyAIApp.Server.Data.Models.SQL.Word", "Word")
                        .WithMany("Meanings")
                        .HasForeignKey("WordId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Word");
                });

            modelBuilder.Entity("JackyAIApp.Server.Data.Models.SQL.WordMeaningTag", b =>
                {
                    b.HasOne("JackyAIApp.Server.Data.Models.SQL.WordMeaning", "WordMeaning")
                        .WithMany("Tags")
                        .HasForeignKey("WordMeaningId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("WordMeaning");
                });

            modelBuilder.Entity("JackyAIApp.Server.Data.Models.SQL.ClozeTest", b =>
                {
                    b.Navigation("Options");
                });

            modelBuilder.Entity("JackyAIApp.Server.Data.Models.SQL.User", b =>
                {
                    b.Navigation("JiraConfigs");

                    b.Navigation("UserWords");
                });

            modelBuilder.Entity("JackyAIApp.Server.Data.Models.SQL.Word", b =>
                {
                    b.Navigation("ClozeTests");

                    b.Navigation("Meanings");

                    b.Navigation("SentenceTests");

                    b.Navigation("TranslationTests");
                });

            modelBuilder.Entity("JackyAIApp.Server.Data.Models.SQL.WordMeaning", b =>
                {
                    b.Navigation("Definitions");

                    b.Navigation("ExampleSentences");

                    b.Navigation("Tags");
                });
#pragma warning restore 612, 618
        }
    }
}
