import os
import subprocess
import json

# Function to list folders in the current directory
def list_folders():
    # List all files and folders in the current directory
    return [f for f in os.listdir() if os.path.isdir(f)]

# Function to generate summary using Ollama
def generate_summary(topic):
    # Call the Ollama API via command line
    # Replace 'ollama' with the appropriate command if necessary (depending on your setup)
    result = subprocess.run(
        ['ollama', 'run', 'mistral', f'create a short text about the following topic: {topic}'], 
        stdout=subprocess.PIPE, 
        stderr=subprocess.PIPE
    )
    
    # Get the result and decode from bytes to string
    summary = result.stdout.decode('utf-8')
    
    # Handle possible errors
    if result.stderr:
        print(f"Error generating summary for {topic}: {result.stderr.decode('utf-8')}")
    
    return summary

# Function to save summary to a file inside the folder
def save_summary(folder_name, summary):
    file_path = os.path.join(folder_name, "summary.txt")
    with open(file_path, 'w') as f:
        f.write(summary)
    print(f"Summary saved to {file_path}")

# Main function to process all folders
def main():
    # List all folders in the current directory
    folders = list_folders()
    
    # Process each folder
    for folder in folders:
        print(f"Processing folder: {folder}")
        summary = generate_summary(folder)  # Generate summary based on folder name
        if summary:
            save_summary(folder, summary)  # Save the summary to a text file

if __name__ == "__main__":
    main()
