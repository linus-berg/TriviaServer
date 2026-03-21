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
            print("Rate limited by API. Waiting 10s...")
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

questions_path = '../questions.json'
all_questions = []
existing_texts = set()

if os.path.exists(questions_path):
    with open(questions_path, 'r') as f:
        all_questions = json.load(f)
        for q in all_questions:
            existing_texts.add(q['QuestionText'].strip().lower())

start_count = len(all_questions)
target_extra = 1500
newly_added = 0
duplicates_skipped = 0

print(f"Starting with {start_count} unique questions.")
print(f"Targeting an additional {target_extra} unique questions.")

while newly_added < target_extra:
    # OpenTDB limits: max 50 per call
    batch_size = 50
    
    print(f"Fetching batch... ({newly_added}/{target_extra} added)")
    questions = fetch_questions(batch_size)
    
    if not questions:
        print("No questions returned, retrying in 5s...")
        time.sleep(5)
        continue
    
    batch_added = 0
    for q in questions:
        text = html.unescape(q['question']).strip().lower()
        if text not in existing_texts:
            new_idx = len(all_questions) + 1
            formatted = format_question(new_idx, q)
            all_questions.append(formatted)
            existing_texts.add(text)
            newly_added += 1
            batch_added += 1
            if newly_added >= target_extra:
                break
        else:
            duplicates_skipped += 1
            
    # Save incrementally after each batch
    if batch_added > 0:
        with open(questions_path, 'w') as f:
            json.dump(all_questions, f, indent=2)
        print(f"  Saved {batch_added} new questions to disk.")
    
    time.sleep(5) # Respect rate limits

print(f"Success! Final total unique questions: {len(all_questions)}")
print(f"Total new questions added this session: {newly_added}")
print(f"Total duplicates skipped: {duplicates_skipped}")
