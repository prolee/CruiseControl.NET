using System;
using System.IO;
using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Text;
using tw.ccnet.core.util;

namespace tw.ccnet.core.sourcecontrol
{	
	public class VssHistoryParser : IHistoryParser
	{		
		internal const string DELIMITER_VERSIONED_START = "*****************  ";
		internal const string DELIMITER_VERSIONED_END = "  *****************";

		internal const string DELIMITER_UNVERSIONED_START = "*****  ";
		internal const string DELIMITER_UNVERSIONED_END = "  *****";


		public Modification[] Parse(TextReader history)
		{
			string[] entries = this.ReadAllEntries(history);
			
			return parseModifications(entries);
		}
		

		internal Modification[] parseModifications(string[] entries)
		{
			// not every entry will yield a valid modification so we can't use
			// an array, but we can assume that most will so starting our 
			// arraylist to be at least as big as the array will save
			// some resizing
			ArrayList modifications = new ArrayList(entries.Length);

			foreach (string entry in entries) 
			{
				VSSParser parser = VSSParserFactory.CreateParser(entry);
				Modification mod = parser.parse();
				if (mod != null)
					modifications.Add(mod);
			}

			return (Modification[]) modifications.ToArray(typeof(Modification));
		}

		internal string[] ReadAllEntries(TextReader history)
		{
			ArrayList entries = new ArrayList();
			string currentLine = history.ReadLine();
			while(IsEndOfFile(currentLine) == false) 
			{
				if(IsEntryDelimiter(currentLine)) 
				{
					StringBuilder b = new StringBuilder();
					b.Append(currentLine).Append("\n");
					currentLine = history.ReadLine();
					while (!IsEntryDelimiter(currentLine))
					{
						b.Append(currentLine).Append("\n");
						currentLine = history.ReadLine();
					}
					entries.Add(b.ToString());
				}
				else 
				{
					currentLine = history.ReadLine();
				}
			}
			return (string[]) entries.ToArray(typeof(string));
		}

		internal bool IsEntryDelimiter(string line) 
		{						
			return IsEndOfFile(line) ||
				(line.StartsWith(DELIMITER_UNVERSIONED_START) && line.EndsWith(DELIMITER_UNVERSIONED_END)) ||
				line.StartsWith(DELIMITER_VERSIONED_START) && line.EndsWith(DELIMITER_VERSIONED_END);
		}

		internal bool IsEndOfFile(string line)
		{
			return line == null;
		}
	}

	internal class VSSParserFactory 
	{
		public static VSSParser CreateParser(string entry) 
		{
			int commentIndex = entry.IndexOf("Comment");
			commentIndex = commentIndex > -1 ? commentIndex : entry.Length;
			string nonCommentEntry = entry.Substring(0, commentIndex);
			if (nonCommentEntry.IndexOf("Checked in") > -1) 
			{
				return new CheckInParser(entry);
			}
			else if (nonCommentEntry.IndexOf("added") > -1) 
			{
				return new AddedParser(entry);
			}
			else if (nonCommentEntry.IndexOf("deleted") > -1)
				return new DeletedParser(entry);
			else if (nonCommentEntry.IndexOf("destroyed") > -1)
				return new DestroyedParser(entry);

			return new NullParser(entry);
		}
	}

	internal abstract class VSSParser 
	{
		protected string entry;
		private static readonly Regex REGEX_USER_DATE_LINE = 
			new Regex(@"User:\s+(\w+)\s+Date:\s+(.+)\s+Time:\s+(.+)$",RegexOptions.Multiline);

		private static readonly Regex REGEX_FILE_NAME = new Regex(@"\*+([\w\s\.]+)", RegexOptions.Multiline);

		private static readonly DateTimeFormatInfo DATE_FORMAT_INFO = CreateDateTimeInfo();

		internal const string DELIMITER_VERSIONED_START = "*****************  ";

		public VSSParser(string entry) 
		{
			this.entry = entry;
		}

		public virtual Modification parse() 
		{
			Modification mod = new Modification();
			setType(mod);
			ParseUsernameAndDate(mod);
			ParseComment(mod);
			mod.FileName = this.parseFileName();
			mod.FolderName = this.parseFolderName();

			return mod;
		}

		internal abstract void setType(Modification mod);

		internal abstract string parseFileName();

		internal void ParseUsernameAndDate(Modification mod)
		{
			Match match = REGEX_USER_DATE_LINE.Match(entry);
			
			mod.UserName = match.Groups[1].Value.Trim();
			
			string date = match.Groups[2].Value.Trim();
			string time = match.Groups[3].Value.Trim();
			// vss gives am and pm as a and p, so we stuff in an m
			string dateAndTime = String.Format("{0};{1}m", date, time);
			mod.ModifiedTime = DateTime.Parse(dateAndTime, DATE_FORMAT_INFO);
		}

		internal void ParseComment(Modification mod)
		{		
			int index = entry.IndexOf("Comment:");
			if (index > -1) 
			{
				mod.Comment = entry.Substring(index + "Comment:".Length).Trim();
			}
		}

		internal virtual string parseFolderName() 
		{
			string folderName = null;
			int checkinIndex = entry.IndexOf("Checked in");
			if (checkinIndex > -1) 
			{
				int commentIndex = entry.IndexOf("Comment:");
				int startIndex = checkinIndex + "Checked in".Length;
				folderName = entry.Substring(startIndex, commentIndex - startIndex).Trim();

			}

			return folderName;
		}

		protected string parseFileNameOther(string type) 
		{
			int timeIndex = entry.IndexOf("Time:");
			int newlineIndex = entry.IndexOf("\n", timeIndex);
			int addedIndex = entry.IndexOf(type, newlineIndex);
			string fileName = entry.Substring(newlineIndex, addedIndex - newlineIndex);
			return fileName.Trim();
		}

		internal string parseFirstLineName() 
		{
			Match match = REGEX_FILE_NAME.Match(entry);

			return match.Groups[1].Value.Trim();
		}

		private static DateTimeFormatInfo CreateDateTimeInfo() 
		{
			DateTimeFormatInfo dfi = new DateTimeFormatInfo();
			dfi.AMDesignator = "a";
			dfi.PMDesignator = "p";
			dfi.MonthDayPattern = @"M-dd-yy;h:mmt";
			return dfi;
		}
	}

	internal class CheckInParser : VSSParser 
	{
		public CheckInParser(string entry) : base(entry){}

		internal override void setType(Modification mod) 
		{
			mod.Type = "checkin";
		}

		internal override string parseFileName() 
		{
			return parseFirstLineName();
		}
	}

	internal class AddedParser : VSSParser 
	{
		private readonly static string type = "added";

		public AddedParser(string entry) : base(entry){}

		public override Modification parse() 
		{
			Modification mod = base.parse();
			if (mod.FileName.StartsWith("$"))
				return null;
			else
				return mod;
		}

		internal override void setType(Modification mod) 
		{
			mod.Type = type;
		}

		internal override string parseFileName() 
		{
			return parseFileNameOther(type);
		}

		internal override string parseFolderName() 
		{
			if (entry.StartsWith(DELIMITER_VERSIONED_START))
				return  "[projectRoot]";
			else
				return parseFirstLineName();
		}
	}

	internal class DeletedParser : VSSParser 
	{
		private readonly static string type = "deleted";

		public DeletedParser(string entry) : base(entry){}

		internal override void setType(Modification mod) 
		{
			mod.Type = type;
		}

		internal override string parseFileName() 
		{
			return parseFileNameOther(type);
		}

		internal override string parseFolderName() 
		{
			if (entry.StartsWith(DELIMITER_VERSIONED_START))
				return  "[projectRoot]";
			else
				return parseFirstLineName();
		}
	}

	internal class DestroyedParser : VSSParser 
	{
		private readonly static string type = "destroyed";

		public DestroyedParser(string entry) : base(entry){}

		internal override void setType(Modification mod) 
		{
			mod.Type = type;
		}

		internal override string parseFileName() 
		{
			return parseFileNameOther(type);
		}

		internal override string parseFolderName() 
		{
			if (entry.StartsWith(DELIMITER_VERSIONED_START))
				return  "[projectRoot]";
			else
				return parseFirstLineName();
		}
	}

	internal class NullParser : VSSParser 
	{
		public NullParser(string entry) : base(entry) {}
		public override Modification parse() 
		{
			return null;
		}

		internal override void setType(Modification mod) 
		{
		}

		internal override string parseFileName() 
		{
			return null;
		}
	}
}
