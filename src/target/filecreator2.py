import os
from pathlib import Path
import ollama #from ollama.chat import ChatModel

# Initialize the chat model (Summarizer equivalent in ollama.chat)
model = ChatModel.from_pretrained('small')

# List all folders in the current directory
directories = [f.name for f in Path().glob('*/') if f.is_dir()]

for dir_name in directories:
    # Get the full path of the current directory
    full_path = os.path.join(os.getcwd(), dir_name)

    # Generate a summary for the folder content (assuming there are text files inside)
    summaries = []
    for file in Path(full_path).glob('*.txt'):
        with open(file, 'r') as f:
            text = f.read()
            response = model.chat(text)  # Summarize the text using the chat model
            summary = response['choices'][0]['text']  # Get the summarized text from the response
            summaries.append(summary)

    # Combine all summaries into one and save it to a .txt file inside the folder
    combined_summary = '\n'.join(summaries)
    with open(os.path.join(full_path, f'{dir_name}_summary.txt'), 'w') as f:
        f.write(combined_summary)
