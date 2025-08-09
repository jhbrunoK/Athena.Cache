using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;

namespace Athena.Cache.Core.Memory;

/// <summary>
/// Memory/Span 기반 고성능 유틸리티 메서드들
/// 제로 allocation을 목표로 하는 메모리 연산 집합
/// </summary>
public static class MemoryUtils
{
    /// <summary>
    /// 문자열을 UTF8 바이트로 변환 (Span 기반)
    /// 작은 문자열은 stackalloc, 큰 문자열은 ArrayPool 사용
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<byte> GetUtf8Bytes(ReadOnlySpan<char> text, Span<byte> buffer, out bool needsReturn, out byte[]? rentedArray)
    {
        needsReturn = false;
        rentedArray = null;
        
        var maxByteCount = Encoding.UTF8.GetMaxByteCount(text.Length);
        
        if (maxByteCount <= buffer.Length)
        {
            // 제공된 버퍼에 맞음 (일반적으로 stackalloc)
            var byteCount = Encoding.UTF8.GetBytes(text, buffer);
            return buffer.Slice(0, byteCount);
        }
        else
        {
            // ArrayPool에서 대여
            rentedArray = ArrayPool<byte>.Shared.Rent(maxByteCount);
            needsReturn = true;
            var byteCount = Encoding.UTF8.GetBytes(text, rentedArray);
            return rentedArray.AsSpan(0, byteCount);
        }
    }
    
    /// <summary>
    /// UTF8 바이트를 문자열로 변환 (zero allocation when possible)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string GetStringFromUtf8(ReadOnlySpan<byte> utf8Bytes)
    {
        return Encoding.UTF8.GetString(utf8Bytes);
    }
    
    /// <summary>
    /// 문자열 배열을 구분자로 결합 (Span 기반)
    /// </summary>
    public static string JoinStrings(ReadOnlySpan<string> parts, char separator)
    {
        if (parts.IsEmpty) return string.Empty;
        if (parts.Length == 1) return parts[0];
        
        // 총 길이 계산
        var totalLength = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            totalLength += parts[i].Length;
            if (i < parts.Length - 1) totalLength++; // 구분자
        }
        
        // 결과 버퍼 생성
        var result = totalLength <= 1024 
            ? stackalloc char[totalLength] 
            : new char[totalLength];
            
        var position = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i].AsSpan();
            part.CopyTo(result.Slice(position));
            position += part.Length;
            
            if (i < parts.Length - 1)
            {
                result[position++] = separator;
            }
        }
        
        return new string(result);
    }
    
    /// <summary>
    /// 정수를 문자열로 변환 (Span 기반, allocation 없음)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string IntToString(int value)
    {
        if (value == 0) return "0";
        
        var isNegative = value < 0;
        if (isNegative) value = -value;
        
        Span<char> buffer = stackalloc char[11]; // int.MaxValue는 10자리 + 부호
        var index = buffer.Length;
        
        while (value > 0)
        {
            buffer[--index] = (char)('0' + (value % 10));
            value /= 10;
        }
        
        if (isNegative)
            buffer[--index] = '-';
            
        return new string(buffer.Slice(index));
    }
    
    /// <summary>
    /// Long을 문자열로 변환 (Span 기반)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string LongToString(long value)
    {
        if (value == 0) return "0";
        
        var isNegative = value < 0;
        if (isNegative) value = -value;
        
        Span<char> buffer = stackalloc char[20]; // long.MaxValue는 19자리 + 부호
        var index = buffer.Length;
        
        while (value > 0)
        {
            buffer[--index] = (char)('0' + (value % 10));
            value /= 10;
        }
        
        if (isNegative)
            buffer[--index] = '-';
            
        return new string(buffer.Slice(index));
    }
    
    /// <summary>
    /// Double을 고정 소수점 문자열로 변환 (Span 기반)
    /// </summary>
    public static string DoubleToFixedString(double value, int decimalPlaces = 2)
    {
        if (double.IsNaN(value)) return "NaN";
        if (double.IsPositiveInfinity(value)) return "∞";
        if (double.IsNegativeInfinity(value)) return "-∞";
        
        var isNegative = value < 0;
        if (isNegative) value = -value;
        
        // 소수점 자리수만큼 곱하기
        var multiplier = Math.Pow(10, decimalPlaces);
        var intValue = (long)Math.Round(value * multiplier);
        
        Span<char> buffer = stackalloc char[32]; // 충분한 버퍼
        var index = buffer.Length;
        
        // 소수 부분
        for (int i = 0; i < decimalPlaces; i++)
        {
            buffer[--index] = (char)('0' + (intValue % 10));
            intValue /= 10;
        }
        
        if (decimalPlaces > 0)
            buffer[--index] = '.';
        
        // 정수 부분
        if (intValue == 0)
        {
            buffer[--index] = '0';
        }
        else
        {
            while (intValue > 0)
            {
                buffer[--index] = (char)('0' + (intValue % 10));
                intValue /= 10;
            }
        }
        
        if (isNegative)
            buffer[--index] = '-';
            
        return new string(buffer.Slice(index));
    }
    
    /// <summary>
    /// 바이트 크기를 사람이 읽기 쉬운 형태로 변환 (Span 기반)
    /// </summary>
    public static string FormatByteSize(long bytes)
    {
        if (bytes == 0) return "0 B";
        
        ReadOnlySpan<string> suffixes = new[] { "B", "KB", "MB", "GB", "TB" };
        var suffixIndex = 0;
        double size = bytes;
        
        while (size >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            size /= 1024;
            suffixIndex++;
        }
        
        var sizeStr = DoubleToFixedString(size, suffixIndex == 0 ? 0 : 1);
        return $"{sizeStr} {suffixes[suffixIndex]}";
    }
    
    /// <summary>
    /// 백분율을 문자열로 변환 (Span 기반)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string FormatPercentage(double ratio, int decimalPlaces = 1)
    {
        var percentage = ratio * 100;
        var percentageStr = DoubleToFixedString(percentage, decimalPlaces);
        return $"{percentageStr}%";
    }
}