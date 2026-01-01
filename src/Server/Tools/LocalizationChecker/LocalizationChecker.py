import re
import sys

def extract_translations_from_View(view_path):
    """Extract all translation strings from file A"""
    translations = {}
    
    try:
        with open(view_path, 'r', encoding='utf-8') as file_a:
            for line_num, line in enumerate(file_a, 1):
                # Match T["..."] patterns
                matches = re.findall(r'T\["([^"]*)"\]', line)
                for match in matches:
                    translations[match] = line_num
    except FileNotFoundError:
        print(f"Error: File {view_path} not found")
        sys.exit(1)
    except Exception as e:
        print(f"Error reading file {view_path}: {e}")
        sys.exit(1)
    
    return translations

def extract_localizations_from_resource_file(file_b_path):
    """Extract all translation strings from file B"""
    translations = set()
    
    try:
        with open(file_b_path, 'r', encoding='utf-8') as file_b:
            for line in file_b:
                # Match the pattern in file B
                match = re.search(r'<data name="([^"]*)"', line)
                if match:
                    translations.add(match.group(1))
    except FileNotFoundError:
        print(f"Error: File {file_b_path} not found")
        sys.exit(1)
    except Exception as e:
        print(f"Error reading file {file_b_path}: {e}")
        sys.exit(1)
    
    return translations

def find_missing_translations(view, resource):
    """Find translations in file A that don't exist in file B"""
    # Extract translations from both files
    file_a_translations = extract_translations_from_View(view)
    file_b_translations = extract_localizations_from_resource_file(resource)
    
    # Find missing translations
    missing_translations = []
    
    for translation_text, line_number in file_a_translations.items():
        if translation_text not in file_b_translations:
            missing_translations.append((translation_text, line_number))
    
    return missing_translations

def main():
    views = ["Shared/_Layout.cshtml", "Home/Index.cshtml", "Home/Searchdomains.cshtml"]
    resources = ["SharedResources.en.resx", "SharedResources.de.resx"]
    
    print("Checking for missing translations...")
    print("=" * 50)
    for view in views:
        for resource in resources:
            missing = find_missing_translations("../../Views/" + view, "../../Resources/" + resource)
            
            if missing:
                print(f"Found {len(missing)} missing translations in {view}:")
                print("-" * 50)
                for translation_text, line_number in missing:
                    print(f"Line {line_number}: T[\"{translation_text}\"]")
            else:
                print(f"All localizations in {view} have a matching resource in {resource}!")

if __name__ == "__main__":
    main()