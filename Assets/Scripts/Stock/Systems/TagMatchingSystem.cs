using System;
using UnityEngine;

namespace Stock.Systems
{
    public class TagMatchingSystem
    {
        /// <summary>
        /// 뉴스 태그와 회사 태그 간의 매칭 점수를 계산합니다. (0.0 ~ 1.0)
        /// </summary>
        public float CalculateTagMatchScore(string[] newsTags, string[] companyTags)
        {
            if (newsTags == null || newsTags.Length == 0 || companyTags == null || companyTags.Length == 0)
                return 0f;

            int matchCount = 0;
            foreach (var nt in newsTags)
            {
                foreach (var ct in companyTags)
                {
                    if (nt.Equals(ct, StringComparison.OrdinalIgnoreCase))
                    {
                        matchCount++;
                        break;
                    }
                }
            }
            
            return Mathf.Clamp01((float)matchCount / newsTags.Length);
        }
    }
}
