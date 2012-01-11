﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SIL.CoreImpl;
using SIL.FieldWorks.Common.COMInterfaces;
using SIL.FieldWorks.FDO.DomainImpl;
using SIL.Utils;

namespace SIL.FieldWorks.FDO.DomainServices
{
	/// <summary>
	/// Morph services
	/// </summary>
	public static class MorphServices
	{
		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Find an allomorph with the specified form, if any. Searches both LexemeForm and
		/// AlternateForms properties.
		/// </summary>
		/// <param name="entry"></param>
		/// <param name="tssform">The tssform.</param>
		/// <returns></returns>
		/// ------------------------------------------------------------------------------------
		public static IMoForm FindMatchingAllomorph(ILexEntry entry, ITsString tssform)
		{
			IMoForm lf = entry.LexemeFormOA;
			int wsVern = TsStringUtils.GetWsAtOffset(tssform, 0);
			string form = tssform.Text;
			if (lf != null && lf.Form.get_String(wsVern).Text == form)
				return lf;
			return entry.AlternateFormsOS.FirstOrDefault(mf => mf.Form.get_String(wsVern).Text == form);
		}

		/// <summary>
		/// Looks through all morphs to find a match. Use the generic interface GetMatchingMorphs(TMoForm, TMoFormRepository)
		/// for better performance.
		/// </summary>
		/// <param name="cache"></param>
		/// <param name="sPrefixMarker"></param>
		/// <param name="tssMorphForm"></param>
		/// <param name="sPostfixMarker"></param>
		/// <returns></returns>
		public static IEnumerable<IMoForm> GetMatchingMorphs(FdoCache cache,
			string sPrefixMarker, ITsString tssMorphForm, string sPostfixMarker)
		{
			return GetMatchingMorphs<IMoForm, IMoFormRepository>(cache, sPrefixMarker, tssMorphForm, sPostfixMarker);
		}

		/// <summary>
		/// This provides matching morphs that are monomorphemic (stems or roots, not bounded)
		/// </summary>
		/// <param name="cache"></param>
		/// <param name="tssMorphForm"></param>
		/// <returns></returns>
		public static IEnumerable<IMoStemAllomorph> GetMatchingMonomorphemicMorphs(FdoCache cache,
			ITsString tssMorphForm)
		{
			return GetMatchingMorphs<IMoStemAllomorph, IMoStemAllomorphRepository>(cache, "", tssMorphForm, "")
				.Where(m => m.MorphTypeRA.Guid != MoMorphTypeTags.kguidMorphBoundRoot &&
							m.MorphTypeRA.Guid != MoMorphTypeTags.kguidMorphBoundStem);
		}

		/// <summary>
		/// Collect instances of a particular subclass of IMoForm corresponding to a collection of TsStrings.
		/// The strings are passed as keys in a dictionary and the results inserted as values in the dictionary.
		/// For each key (typically a wordform form), we want to find an MoForm that is the LexemeForm or
		/// one of the alternate forms of an Entry (these are currently the only possible owners, so we don't need
		/// to check), which has a form matching the TsString in the appropriate writing system,
		/// and which is not an affix.
		/// Note: depends on being able to look things up in the dictionary using TsStrings as keys.
		/// TsStrings do not currently implement == properly.
		/// Therefore, you need to make a special dictionary with an equals test that uses ITsString.Equals.
		/// </summary>
		public static void GetMatchingMonomorphemicMorphs(FdoCache cache, Dictionary<ITsString, IMoStemAllomorph> formCollector)
		{
			var wss = (from key in formCollector.Keys select TsStringUtils.GetWsAtOffset(key, 0)).Distinct().ToArray();
			var morphRepo = (Infrastructure.Impl.MoStemAllomorphRepository)cache.ServiceLocator.GetInstance<IMoStemAllomorphRepository>();
			var morphData = morphRepo.MonomorphemicMorphData();
			foreach (var key in formCollector.Keys.ToArray())
			{
				int ws = TsStringUtils.GetWsAtOffset(key, 0);
				string form = key.Text;
				var dataKey = new Tuple<int, string>(ws, form);
				IMoStemAllomorph morph;
				if (morphData.TryGetValue(dataKey, out morph))
					formCollector[key] = morph;
			}
		}

		/// <summary>
		/// </summary>
		/// <param name="cache"></param>
		/// <param name="sPrefixMarker"></param>
		/// <param name="tssMorphForm"></param>
		/// <param name="sPostfixMarker"></param>
		/// <returns></returns>
		public static IEnumerable<TMoForm> GetMatchingMorphs<TMoForm, TMoFormRepository>(FdoCache cache,
			string sPrefixMarker, ITsString tssMorphForm, string sPostfixMarker)
			where TMoForm : IMoForm
			where TMoFormRepository : IRepository<TMoForm>
		{
			var allMorphs =
				cache.ServiceLocator.GetInstance<TMoFormRepository>().AllInstances();
			// If the morph is either a proclitic or an enclitic, then it can stand alone; it does not have to have any
			// prefix or postfix even when such is defined for proclitic and/or enclitc.  So we augment the query to allow
			// these two types to be found without the appropriate prefix or postfix.  See LT-8124.
			// Restrict by Morph Type as well as Morph Form.
			int wsForm = TsStringUtils.GetWsAtOffset(tssMorphForm, 0);
			return from mf in allMorphs
				   where mf.Owner != null && mf.Owner.IsValidObject /* check for orphans See UndoAllIssueTest */&&
						 // computing OwningFlid is relatively slow, especially twice times 46000...
						 //(mf.OwningFlid == LexEntryTags.kflidAlternateForms ||
						 // mf.OwningFlid == LexEntryTags.kflidLexemeForm) &&
						 mf.Owner.ClassID == LexEntryTags.kClassId &&
						 tssMorphForm.Equals(mf.Form.get_String(wsForm)) &&
						 mf.MorphTypeRA != null &&
						 (mf.MorphTypeRA.Prefix == sPrefixMarker ||
						  sPrefixMarker == "" && (mf.MorphTypeRA.Prefix == null ||
												  mf.MorphTypeRA.Guid == MoMorphTypeTags.kguidMorphEnclitic ||
												  mf.MorphTypeRA.Guid == MoMorphTypeTags.kguidMorphProclitic)) &&
						 (mf.MorphTypeRA.Postfix == sPostfixMarker ||
						  sPostfixMarker == "" && (mf.MorphTypeRA.Postfix == null ||
												   mf.MorphTypeRA.Guid == MoMorphTypeTags.kguidMorphEnclitic ||
												   mf.MorphTypeRA.Guid == MoMorphTypeTags.kguidMorphProclitic))
				   select mf;
		}

		/// <summary>
		/// Make a new MoForm (actually the appropriate subclass, as deduced by FindMorphType
		/// from fullForm), add it to the morphemes of the owning lex entry, set its
		/// MoMorphType, also as deduced by FindMorphType from fullForm, and also set the form
		/// itself.
		/// If the entry doesn't already have a lexeme form, put the new morph there.
		/// </summary>
		/// <param name="owningEntry"></param>
		/// <param name="fullForm">uses default vernacular writing system</param>
		/// <returns></returns>
		public static IMoForm MakeMorph(ILexEntry owningEntry, string fullForm)
		{
			return MakeMorph(
				owningEntry,
				owningEntry.Cache.TsStrFactory.MakeString(
					fullForm,
					owningEntry.Cache.DefaultVernWs));
		}

		/// <summary>
		/// Make a new MoForm (actually the appropriate subclass, as deduced by FindMorphType
		/// from fullForm), add it to the morphemes of the owning lex entry, set its
		/// MoMorphType, also as deduced by FindMorphType from tssfullForm, and also set the form
		/// itself.
		/// If the entry doesn't already have a lexeme form, put the new morph there.
		/// </summary>
		/// <param name="owningEntry"></param>
		/// <param name="tssfullForm">uses the ws of tssfullForm</param>
		/// <returns></returns>
		public static IMoForm MakeMorph(ILexEntry owningEntry, ITsString tssfullForm)
		{
			int clsidForm; // The subclass of MoMorph to create if we need a new object.
			var realForm = tssfullForm.Text; // Gets stripped of morpheme-type characters.
			var mmt = FindMorphType(owningEntry.Cache, ref realForm, out clsidForm);
			//MoForm allomorph = null;
			IMoForm allomorph;
			switch (clsidForm)
			{
				case MoStemAllomorphTags.kClassId:
					allomorph = new MoStemAllomorph();
					break;
				case MoAffixAllomorphTags.kClassId:
					allomorph = new MoAffixAllomorph();
					break;
				default:
					throw new InvalidProgramException(
						"unexpected MoForm subclass returned from FindMorphType");
			}
			if (owningEntry.LexemeFormOA == null)
				owningEntry.LexemeFormOA = allomorph;
			else
			{
				// An earlier version inserted at the start, to avoid making it the default
				// underlying form, which was the last one. But now we have an explicit
				// lexeme form. So go ahead and put it at the end.
				owningEntry.AlternateFormsOS.Add(allomorph);
			}
			allomorph.MorphTypeRA = mmt; // Has to be done, before the next call.

			var wsVern = TsStringUtils.GetWsAtOffset(tssfullForm, 0);
			allomorph.Form.set_String(wsVern,
							owningEntry.Cache.TsStrFactory.MakeString(EnsureNoMarkers(tssfullForm.Text, owningEntry.Cache), wsVern));
			return allomorph;
		}

		/// <summary>
		///  Do something
		/// </summary>
		/// <param name="form"></param>
		/// <param name="cache"></param>
		/// <returns>string</returns>
		public static string EnsureNoMarkers(string form, FdoCache cache)
		{
			return StripAffixMarkers(cache, form);
		}

		/// <summary>
		/// Get the MoMorphType objects for the major affix types.
		/// </summary>
		/// <param name="cache"></param>
		/// <param name="mmtPrefix"></param>
		/// <param name="mmtSuffix"></param>
		/// <param name="mmtInfix"></param>
		public static void GetMajorAffixMorphTypes(FdoCache cache, out IMoMorphType mmtPrefix,
												   out IMoMorphType mmtSuffix, out IMoMorphType mmtInfix)
		{
			mmtPrefix = null;
			mmtSuffix = null;
			mmtInfix = null;

			foreach (IMoMorphType mmt in cache.LanguageProject.LexDbOA.MorphTypesOA.PossibilitiesOS)
			{
				switch (mmt.Guid.ToString())
				{
					case MoMorphTypeTags.kMorphPrefix:
						mmtPrefix = mmt;
						break;
					case MoMorphTypeTags.kMorphSuffix:
						mmtSuffix = mmt;
						break;
					case MoMorphTypeTags.kMorphInfix:
						mmtInfix = mmt;
						break;
				}
			}
		}

		/// <summary>
		/// Get the morph type and class ID for the given input string. Trims fullForm if needed.
		/// </summary>
		/// <param name="cache">The cache to look in.</param>
		/// <param name="fullForm">The MoForm form, plus optional key characters before and/or after the form.</param>
		/// <param name="clsidForm">Return the clsid for the form.</param>
		/// <returns>The MoMorphType indicated by the possible markers.</returns>
		/// <exception cref="ArgumentException">
		/// Thrown in the following cases:
		/// 1. The input form is an empty string,
		/// 2. The imput form is improperly marked according to the current settings of the
		///		MoMorphType objects.
		/// </exception>
		public static IMoMorphType FindMorphType(FdoCache cache, ref string fullForm, out int clsidForm)
		{
			Debug.Assert(cache != null);
			Debug.Assert(fullForm != null);

			clsidForm = MoStemAllomorphTags.kClassId;	// default
			IMoMorphType mt = null;
			fullForm = fullForm.Trim();
			if (fullForm.Length == 0)
				throw new ArgumentException("The form is empty.", "fullForm");

			string sLeading;
			string sTrailing;
			GetAffixMarkers(cache, fullForm, out sLeading, out sTrailing);

			/*
			 Not dealt with.
			 particle	(ambiguous: particle, circumfix, root, stem, clitic)
			 circumfix	(ambiguous: particle, circumfix, root, stem, clitic)
			 root		(ambiguous: particle, circumfix, root, stem, clitic)
			 clitic		(ambiguous: particle, circumfix, root, stem, clitic)
			 bound root	(ambiguous: bound root, bound stem)
			 infixing interfix		(ambiguous: infixing interfix, infix)
			 prefixing interfix		(ambiguous: prefixing interfix, prefix)
			 suffixing interfix		(ambiguous: suffixing interfix, suffix)
			 discontiguous phrase	(ambiguous: discontiguous phrase, phrase)
			 End of not dealt with.

			 What we do deal with.
			 prefix-	(ambiguous: prefixing interfix, prefix)
			 =simulfix=
			 -suffix	(ambiguous: suffixing interfix, suffix)
			 -infix-	(ambiguous: infixing interfix, infix)
			 ~suprafix~
			 =enclitic
			 proclitic=
			 *bound stem	(ambiguous: bound root, bound stem)
			 stem		(ambiguous: particle, circumfix, root, stem, clitic)
			 phrase		(ambiguous: discontiguous phrase, phrase)
			 End of what we do deal with.

			For ambiguous cases, pick 'root' and 'bound root', as per LarryH's suggestion on 11/18/2003.
			(Changed: May, 2004). For ambiguous cases, pick 'stem' and 'bound stem',
			as per WordWorks May, 2004 meeting (Andy Black & John Hatton).
			For ambiguous cases, pick 'infix', 'prefix', and 'suffix'.
			 */
			foreach (IMoMorphType mmt in cache.LanguageProject.LexDbOA.MorphTypesOA.PossibilitiesOS)
			{
				if (sLeading == mmt.Prefix && sTrailing == mmt.Postfix)
				{
					// handle ambiguous cases
					var morphTypeRep = cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>();
					switch (mmt.Guid.ToString())
					{
						case MoMorphTypeTags.kMorphPrefixingInterfix:
							mt = morphTypeRep.GetObject(MoMorphTypeTags.kguidMorphPrefix);
							break;

						case MoMorphTypeTags.kMorphSuffixingInterfix:
							mt = morphTypeRep.GetObject(MoMorphTypeTags.kguidMorphSuffix);
							break;

						case MoMorphTypeTags.kMorphInfixingInterfix:
							mt = morphTypeRep.GetObject(MoMorphTypeTags.kguidMorphInfix);
							break;

						case MoMorphTypeTags.kMorphBoundRoot:
							mt = morphTypeRep.GetObject(MoMorphTypeTags.kguidMorphBoundStem);
							break;

						case MoMorphTypeTags.kMorphParticle:
						case MoMorphTypeTags.kMorphCircumfix:
						case MoMorphTypeTags.kMorphRoot:
						case MoMorphTypeTags.kMorphDiscontiguousPhrase:
						case MoMorphTypeTags.kMorphPhrase:
						case MoMorphTypeTags.kMorphClitic:
							mt = morphTypeRep.GetObject(MoMorphTypeTags.kguidMorphStem);
							break;

						default:
							mt = mmt;
							break;
					}

					// handle phrase
					if (mt.Guid == MoMorphTypeTags.kguidMorphStem && fullForm.IndexOf(" ") != -1)
						mt = morphTypeRep.GetObject(MoMorphTypeTags.kguidMorphPhrase);

					if (mt.IsAffixType)
						clsidForm = MoAffixAllomorphTags.kClassId;
					break;
				}
			}

			if (mt == null)
			{
				if (sLeading == null && sTrailing == null)
					throw new InvalidOperationException(String.Format(Strings.ksInvalidUnmarkedForm0, fullForm));
				if (sLeading == null)
					throw new InvalidOperationException(String.Format(Strings.ksInvalidForm0Trailing1, fullForm, sTrailing));
				if (sTrailing == null)
					throw new InvalidOperationException(String.Format(Strings.ksInvalidForm0Leading1, fullForm, sLeading));
				throw new InvalidOperationException(String.Format(Strings.ksInvalidForm0Leading1Trailing2, fullForm, sLeading, sTrailing));
			}

			if (sLeading != null)
				fullForm = fullForm.Substring(sLeading.Length);
			if (sTrailing != null)
				fullForm = fullForm.Substring(0, fullForm.Length - sTrailing.Length);

			return mt;
		}

		/// <summary>
		/// Breaks the given formWithMarkers into its basic components: MorphType, Prefix, Postfix, and TssForm.
		/// </summary>
		/// <param name="cache"></param>
		/// <param name="formWithMarkers"></param>
		/// <param name="guidDefaultMorphType">a default MorphType if the given entryForm is empty or isn't in current vernacular</param>
		/// <returns></returns>
		public static MorphComponents BuildMorphComponents(FdoCache cache, ITsString formWithMarkers, Guid guidDefaultMorphType)
		{
			var morphComponents = new MorphComponents();
			// First determine morphType from given fullMorphForm
			// Check whether the incoming form is vernacular or analysis.
			// (See LT-4074 and LT-7240.)
			bool fVern = cache.ServiceLocator.WritingSystems.CurrentVernacularWritingSystems.Contains(TsStringUtils.GetWsAtOffset(formWithMarkers, 0));
			// If form is empty (cf. LT-1621), use stem
			if (formWithMarkers.Length == 0 || !fVern)
			{
				morphComponents.MorphType = cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>().GetObject(guidDefaultMorphType);
				morphComponents.Prefix = null;
				morphComponents.TssForm = null;
				morphComponents.Postfix = null;
			}
			else
			{
				// load
				int clsidForm;
				string form = formWithMarkers.Text;
				morphComponents.MorphType = FindMorphType(cache, ref form, out clsidForm);
				string prefix;
				string postfix;
				GetAffixMarkers(cache, formWithMarkers.Text, out prefix, out postfix);
				morphComponents.Prefix = prefix;
				morphComponents.TssForm = TsStringUtils.MakeTss(form, TsStringUtils.GetWsAtOffset(formWithMarkers, 0));
				morphComponents.Postfix = postfix;
			}
			return morphComponents;
		}

		/// <summary>
		/// Initialize a new LexEntryComponents computing MorphType and LexFormAlternatives[0] from formWithMarkers
		/// </summary>
		/// <param name="cache"></param>
		/// <param name="formWithMarkers"></param>
		/// <returns></returns>
		public static LexEntryComponents BuildEntryComponents(FdoCache cache, ITsString formWithMarkers)
		{
			var morphComponents = BuildMorphComponents(cache, formWithMarkers, MoMorphTypeTags.kguidMorphStem);
			var entryComponents = new LexEntryComponents {MorphType = morphComponents.MorphType};
			if (morphComponents.TssForm != null)
				entryComponents.LexemeFormAlternatives.Add(morphComponents.TssForm);
			return entryComponents;
		}

		/// <summary>
		/// If the given form matches exactly one of the morphtype prefixes, return that
		/// MoMorphType object.  If that morphtype also has a postfix, add the postfix to
		/// the adjusted form.  This allows better handling of suprafixes for the default
		/// prefix and postfix marking.  (See LT-6081 and LT-6082.)
		/// </summary>
		/// <param name="cache">The cache.</param>
		/// <param name="sForm">The form.</param>
		/// <param name="sAdjustedForm">The adjusted form.</param>
		/// <returns></returns>
		public static IMoMorphType GetTypeIfMatchesPrefix(FdoCache cache, string sForm, out string sAdjustedForm)
		{
			sAdjustedForm = sForm;
			IMoMorphType mmtPossible = null;
			IMoMorphType mmtBoundRoot = null;
			foreach (IMoMorphType mmt in cache.LanguageProject.LexDbOA.MorphTypesOA.PossibilitiesOS)
			{
				if (mmt.Guid == MoMorphTypeTags.kguidMorphBoundRoot)
				{
					// save bound root for last to allow bound stem to have priority.
					mmtBoundRoot = mmt;
				}
				else if (mmt.Prefix == sForm)
				{
					// If there's a type with a matching prefix and no postfix, return it.  Don't
					// worry about ambiguity -- that's life.
					if (mmt.Postfix == null)
						return mmt;
					// We have both a prefix and a postfix.  Save it in case it's unique.
					mmtPossible = mmt;
				}
			}
			if (mmtBoundRoot != null && mmtBoundRoot.Prefix == sForm && mmtBoundRoot.Postfix == null)
				return mmtBoundRoot;

			if (mmtPossible != null)
			{
				sAdjustedForm = mmtPossible.Prefix + mmtPossible.Postfix;
				return mmtPossible;
			}
			return null;
		}

		static private void GetAffixMarkers(FdoCache cache, string fullForm, out string prefixMarker,
											out string postfixMarker)
		{
			var prefixMarkers = PrefixMarkers(cache);
			var postfixMarkers = PostfixMarkers(cache);

			prefixMarker = null;
			postfixMarker = null;
			int iMatchedPrefix;
			int iMatchedPostfix;
			IdentifyAffixMarkers(fullForm, prefixMarkers, postfixMarkers, out iMatchedPrefix, out iMatchedPostfix);
			if (iMatchedPrefix >= 0)
				prefixMarker = prefixMarkers[iMatchedPrefix];
			if (iMatchedPostfix >= 0)
				postfixMarker = postfixMarkers[iMatchedPostfix];
		}

		/// <summary>
		/// Return a stripped form of a (full)form which may contain affix markers.
		/// </summary>
		/// <param name="cache"></param>
		/// <param name="fullForm">string containing affix markers</param>
		/// <returns>string without the prefix and postfix markers</returns>
		internal static string StripAffixMarkers(FdoCache cache, string fullForm)
		{
			var prefixMarkers = PrefixMarkers(cache);
			var postfixMarkers = PostfixMarkers(cache);

			if (!String.IsNullOrEmpty(fullForm))
			{
				string strippedForm = fullForm.Trim();
				int iMatchedPrefix;
				int iMatchedPostfix;
				IdentifyAffixMarkers(fullForm, prefixMarkers, postfixMarkers, out iMatchedPrefix, out iMatchedPostfix);
				// cut out leading type marker
				if (iMatchedPrefix >= 0)
					strippedForm = strippedForm.Substring(prefixMarkers[iMatchedPrefix].Length);
				// cut out trailing morpheme type marker
				if (iMatchedPostfix >= 0)
				{
					strippedForm = strippedForm.Substring(0,
														  strippedForm.Length - postfixMarkers[iMatchedPostfix].Length);
				}
				return strippedForm;
			}

			return "";
		}

		/// <summary>
		/// Return the list of strings used to mark morphemes at the beginning. These must not occur as part
		/// of the text of a morpheme (except at the boundaries to indicate its type).
		/// </summary>
		/// <param name="cache"></param>
		/// <returns></returns>
		public static string[] PrefixMarkers(FdoCache cache)
		{
			var rgMarkers = new List<string>();
			foreach (IMoMorphType mmt in cache.LanguageProject.LexDbOA.MorphTypesOA.PossibilitiesOS)
			{
				if (!String.IsNullOrEmpty(mmt.Prefix) && !rgMarkers.Contains(mmt.Prefix))
					rgMarkers.Add(mmt.Prefix);
			}
			return rgMarkers.ToArray();
		}

		/// <summary>
		/// Return the list of strings used to mark morphemes at the end. These must not occur as part
		/// of the text of a morpheme (except at the boundaries to indicate its type).
		/// </summary>
		/// <param name="cache"></param>
		/// <returns></returns>
		public static string[] PostfixMarkers(FdoCache cache)
		{
			var rgMarkers = new List<string>();
			foreach (IMoMorphType mmt in cache.LanguageProject.LexDbOA.MorphTypesOA.PossibilitiesOS)
			{
				if (!String.IsNullOrEmpty(mmt.Postfix) && !rgMarkers.Contains(mmt.Postfix))
					rgMarkers.Add(mmt.Postfix);
			}
			return rgMarkers.ToArray();
		}

		private static void IdentifyAffixMarkers(string fullForm, string[] prefixMarkers, string[] postfixMarkers,
												 out int iMatchedPrefix, out int iMatchedPostfix)
		{
			var ichPrefixMatch = MiscUtils.IndexOfAnyString(fullForm, prefixMarkers, out iMatchedPrefix, StringComparison.Ordinal);
			// Prefix must match on first character.
			if (ichPrefixMatch != 0)
				iMatchedPrefix = -1;
			iMatchedPostfix = -1;
			for (var i = 0; i < postfixMarkers.Length; ++i)
			{
				var ichPostfixMatch = fullForm.LastIndexOf(postfixMarkers[i]);
				if (ichPostfixMatch <= 0 || ichPostfixMatch + postfixMarkers[i].Length != fullForm.Length) continue;

				if (iMatchedPostfix == -1)
					iMatchedPostfix = i;
				else if (postfixMarkers[i].Length > postfixMarkers[iMatchedPostfix].Length)
					iMatchedPostfix = i;
			}
		}

		/// <summary>
		/// Determine whether the given object is an affix type morph type.
		/// </summary>
		/// <param name="cache"></param>
		/// <param name="hvoMorphType"></param>
		/// <returns></returns>
		public static bool IsAffixType(FdoCache cache, int hvoMorphType)
		{
			return cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>().GetObject(hvoMorphType).IsAffixType;
		}

		/// <summary>
		/// Determine whether the given object is a "prefix-ish" morph type.
		/// </summary>
		/// <param name="cache"></param>
		/// <param name="hvoMorphType"></param>
		/// <returns></returns>
		public static bool IsPrefixishType(FdoCache cache, int hvoMorphType)
		{
			return cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>().GetObject(hvoMorphType).IsPrefixishType;
		}

		/// <summary>
		/// Determine whether the given object is a "suffix-ish" morph type.
		/// </summary>
		/// <param name="cache"></param>
		/// <param name="hvoMorphType"></param>
		/// <returns></returns>
		public static bool IsSuffixishType(FdoCache cache, int hvoMorphType)
		{
			return cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>().GetObject(hvoMorphType).IsSuffixishType;
		}
	}
}