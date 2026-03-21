import requests
import json
import time
import html
import os

def fetch_questions(amount=50):
    url = f"https://opentdb.com/api.php?amount={amount}&type=multiple"
    try:
        response = requests.get(url)
        if response.status_code == 200:
            return response.json().get('results', [])
        elif response.status_code == 429:
            print("Rate limited. Waiting 10s...")
            time.sleep(10)
    except Exception as e:
        print(f"Error fetching: {e}")
    return []

def format_question(idx, q):
    options = q['incorrect_answers'] + [q['correct_answer']]
    import random
    random.shuffle(options)
    
    letters = ['A', 'B', 'C', 'D']
    formatted_options = []
    answer_letter = ""
    
    for i, opt in enumerate(options):
        letter = letters[i]
        text = html.unescape(opt)
        formatted_options.append(f"{letter}) {text}")
        if opt == q['correct_answer']:
            answer_letter = letter
            
    return {
        "Id": idx,
        "QuestionText": html.unescape(q['question']),
        "Options": formatted_options,
        "Answer": answer_letter
    }

questions_path = 'questions.json'
all_questions = []
existing_texts = set()

if os.path.exists(questions_path):
    with open(questions_path, 'r') as f:
        all_questions = json.load(f)
        for q in all_questions:
            existing_texts.add(q['QuestionText'].strip().lower())

print(f"Starting with {len(all_questions)} unique questions.")
target_total = 5000
newly_added = 0
duplicates_skipped = 0

while len(all_questions) < target_total:
    remaining_needed = target_total - len(all_questions)
    batch_size = min(50, remaining_needed + 10) # Fetch a bit more to account for potential duplicates
    
    print(f"Fetching batch (Current: {len(all_questions)}/{target_total})...")
    questions = fetch_questions(batch_size)
    
    if not questions:
        print("No questions returned, retrying in 5s...")
        time.sleep(5)
        continue
        
    for q in questions:
        text = html.unescape(q['question']).strip().lower()
        if text not in existing_texts:
            new_idx = len(all_questions) + 1
            formatted = format_question(new_idx, q)
            all_questions.append(formatted)
            existing_texts.add(text)
            newly_added += 1
            if len(all_questions) >= target_total:
                break
        else:
            duplicates_skipped += 1
            
    print(f"Added {newly_added} new. Skipped {duplicates_skipped} duplicates so far.")
    
    # Save incrementally every few batches just in case
    if newly_added % 200 == 0:
        with open(questions_path, 'w') as f:
            json.dump(all_questions, f, indent=2)
            
    time.sleep(5) # Respect rate limit

with open(questions_path, 'w') as f:
    json.dump(all_questions, f, indent=2)

print(f"Success! Total unique questions: {len(all_questions)}")
print(f"Total new questions added this session: {newly_added}")
print(f"Total duplicates skipped: {duplicates_skipped}")
