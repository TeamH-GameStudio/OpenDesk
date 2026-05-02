using System;
using AgentCreationTest.Models;
using UnityEngine;
using WardrobeModel = AgentCreationTest.Models.Wardrobe;

namespace OpenDesk.Characters.Wardrobe.Persistence
{
    /// <summary>
    /// 한 캐릭터의 아웃핏 스냅샷.<br/>
    /// 각 슬롯은 <see cref="WardrobePartOptionSO.Id"/> 문자열로 저장한다 — 카탈로그 옵션 순서가 바뀌어도 데이터가 깨지지 않는다.<br/>
    /// 빈 문자열은 "카탈로그 기본값 사용"을 의미한다.<br/>
    /// JsonUtility 직렬화를 위해 [Serializable] + 공개 필드 기반.
    /// </summary>
    [Serializable]
    public sealed class WardrobeOutfit
    {
        public string Skin = string.Empty;
        public string Hair = string.Empty;
        public string Eyes = string.Empty;
        public string Mouth = string.Empty;
        public string Top = string.Empty;
        public string Bottom = string.Empty;
        public string Shoes = string.Empty;

        /// <summary>
        /// 빈 outfit (모든 슬롯 = empty string). 런타임에서 카탈로그를 통해 해석될 때
        /// 각 슬롯의 <see cref="WardrobeCatalogSO"/> DefaultOption으로 폴백한다.<br/>
        /// 매번 새 인스턴스를 반환 — outfit은 mutable이라 공유 인스턴스를 노출하면
        /// 호출자가 의도치 않게 글로벌 default를 변경할 수 있음.
        /// </summary>
        public static WardrobeOutfit Default => new WardrobeOutfit();

        public string Get(WardrobePart part)
        {
            switch (part)
            {
                case WardrobePart.Skin:   return Skin;
                case WardrobePart.Hair:   return Hair;
                case WardrobePart.Eyes:   return Eyes;
                case WardrobePart.Mouth:  return Mouth;
                case WardrobePart.Top:    return Top;
                case WardrobePart.Bottom: return Bottom;
                case WardrobePart.Shoes:  return Shoes;
                default: return string.Empty;
            }
        }

        /// <summary>
        /// 단일 슬롯만 갱신한 새 인스턴스를 반환한다 (immutable update 패턴).
        /// </summary>
        public WardrobeOutfit With(WardrobePart part, string optionId)
        {
            var copy = Clone();
            var safe = optionId ?? string.Empty;
            switch (part)
            {
                case WardrobePart.Skin:   copy.Skin   = safe; break;
                case WardrobePart.Hair:   copy.Hair   = safe; break;
                case WardrobePart.Eyes:   copy.Eyes   = safe; break;
                case WardrobePart.Mouth:  copy.Mouth  = safe; break;
                case WardrobePart.Top:    copy.Top    = safe; break;
                case WardrobePart.Bottom: copy.Bottom = safe; break;
                case WardrobePart.Shoes:  copy.Shoes  = safe; break;
            }
            return copy;
        }

        public WardrobeOutfit Clone() => new WardrobeOutfit
        {
            Skin = Skin, Hair = Hair, Eyes = Eyes, Mouth = Mouth,
            Top = Top, Bottom = Bottom, Shoes = Shoes,
        };

        /// <summary>
        /// 인덱스 기반 <see cref="WardrobeModel"/> 모델을 카탈로그를 통해 ID 기반 outfit으로 변환한다.<br/>
        /// 옵션이 null이면 빈 문자열(=기본값 사용).
        /// </summary>
        public static WardrobeOutfit FromCatalog(WardrobeCatalogSO catalog, WardrobeModel indices)
        {
            var o = new WardrobeOutfit();
            if (catalog == null || indices == null) return o;

            o.Skin   = ResolveId(catalog, WardrobePart.Skin,   indices.Skin);
            o.Hair   = ResolveId(catalog, WardrobePart.Hair,   indices.Hair);
            o.Eyes   = ResolveId(catalog, WardrobePart.Eyes,   indices.Eyes);
            o.Mouth  = ResolveId(catalog, WardrobePart.Mouth,  indices.Mouth);
            o.Top    = ResolveId(catalog, WardrobePart.Top,    indices.Top);
            o.Bottom = ResolveId(catalog, WardrobePart.Bottom, indices.Bottom);
            o.Shoes  = ResolveId(catalog, WardrobePart.Shoes,  indices.Shoes);
            return o;
        }

        /// <summary>
        /// 저장된 ID들을 카탈로그를 통해 인덱스 기반 <see cref="WardrobeModel"/>로 복원한다.<br/>
        /// ID가 비어있거나 카탈로그에서 찾을 수 없으면 카탈로그의 기본 옵션 인덱스를 사용.<br/>
        /// 결과는 <see cref="WardrobeApplier.Apply"/>에 그대로 넘길 수 있다.
        /// </summary>
        public WardrobeModel ToWardrobe(WardrobeCatalogSO catalog)
        {
            if (catalog == null) return WardrobeModel.Default;
            return new WardrobeModel(
                skin:   ResolveIndex(catalog, WardrobePart.Skin,   Skin),
                hair:   ResolveIndex(catalog, WardrobePart.Hair,   Hair),
                eyes:   ResolveIndex(catalog, WardrobePart.Eyes,   Eyes),
                mouth:  ResolveIndex(catalog, WardrobePart.Mouth,  Mouth),
                top:    ResolveIndex(catalog, WardrobePart.Top,    Top),
                bottom: ResolveIndex(catalog, WardrobePart.Bottom, Bottom),
                shoes:  ResolveIndex(catalog, WardrobePart.Shoes,  Shoes));
        }

        private static string ResolveId(WardrobeCatalogSO catalog, WardrobePart part, int index)
        {
            var option = catalog.Resolve(part, index);
            return option != null ? (option.Id ?? string.Empty) : string.Empty;
        }

        private static int ResolveIndex(WardrobeCatalogSO catalog, WardrobePart part, string id)
        {
            if (string.IsNullOrEmpty(id)) return catalog.IndexOfDefault(part);

            var options = catalog.GetOptions(part);
            if (options == null) return catalog.IndexOfDefault(part);

            for (int i = 0; i < options.Count; i++)
            {
                if (options[i] != null && options[i].Id == id)
                    return i;
            }

            // ID는 있지만 카탈로그에서 사라진 경우 — 기본값으로 graceful fallback.
            Debug.LogWarning($"[WardrobeOutfit] {part} 슬롯 ID '{id}'를 카탈로그에서 찾지 못했습니다. 기본 옵션으로 대체합니다.");
            return catalog.IndexOfDefault(part);
        }
    }
}
